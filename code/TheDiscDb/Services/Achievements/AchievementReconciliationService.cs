namespace TheDiscDb.Services.Achievements;

using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using TheDiscDb.Web.Data;

/// <summary>
/// Nightly reconciliation for achievements. On first run it performs the one-time launch
/// backfill (awarding everything already earned from existing history); thereafter it
/// re-evaluates every account on a daily cadence to catch anything the event-driven path
/// missed. All awarding is idempotent, so a reconciliation pass never double-awards.
/// </summary>
public sealed class AchievementReconciliationService(
    IServiceScopeFactory scopeFactory,
    ILogger<AchievementReconciliationService> logger) : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromHours(24);
    private static readonly TimeSpan StartupDelay = TimeSpan.FromMinutes(2);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Let the app finish starting and the database migration complete before the backfill.
        try
        {
            await Task.Delay(StartupDelay, stoppingToken);
        }
        catch (OperationCanceledException)
        {
            return;
        }

        bool firstRun = true;

        using var timer = new PeriodicTimer(Interval);
        do
        {
            var actor = firstRun ? AchievementAuditActor.Backfill : AchievementAuditActor.Reconciliation;
            try
            {
                await ReconcileAllAsync(actor, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Achievement reconciliation pass failed.");
            }

            firstRun = false;
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }

    private async Task ReconcileAllAsync(AchievementAuditActor actor, CancellationToken cancellationToken)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var dbFactory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<SqlServerDataContext>>();
        var achievements = scope.ServiceProvider.GetRequiredService<IAchievementService>();

        string[] userIds;
        await using (var db = await dbFactory.CreateDbContextAsync(cancellationToken))
        {
            userIds = await db.Users.Select(u => u.Id).ToArrayAsync(cancellationToken);
        }

        logger.LogInformation("Achievement reconciliation ({Actor}) starting for {Count} users.", actor, userIds.Length);

        int awarded = 0;
        foreach (var userId in userIds)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                var result = await achievements.EvaluateUserAsync(userId, actor, cancellationToken);
                awarded += result.NewlyAwarded;
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Achievement reconciliation failed for user {UserId}.", userId);
            }
        }

        logger.LogInformation("Achievement reconciliation ({Actor}) complete: {Awarded} new awards.", actor, awarded);
    }
}
