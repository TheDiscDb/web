using System.Diagnostics;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using TheDiscDb.Web.Data;

namespace TheDiscDb.DatabaseMigration;

public class Worker(
    IServiceProvider serviceProvider,
    IHostApplicationLifetime hostApplicationLifetime,
    SeedingHealthCheck seedingHealthCheck,
    ILogger<Worker> logger) : BackgroundService
{
    public const string ActivitySourceName = "Migrations";
    private static readonly ActivitySource activitySource = new(ActivitySourceName);

    protected override async Task ExecuteAsync(CancellationToken cancellationToken)
    {
        using var activity = activitySource.StartActivity("Migrating database", ActivityKind.Client);

        try
        {
            SeedPlan? seedPlan = null;

            using (var scope = serviceProvider.CreateScope())
            {
                var dbContext = scope.ServiceProvider.GetRequiredService<SqlServerDataContext>();
                var options = scope.ServiceProvider.GetRequiredService<IOptions<DatabaseMigrationOptions>>();

                await RunMigrationAsync(dbContext, cancellationToken);

                bool hasData = await dbContext.MediaItems.AnyAsync(cancellationToken);
                var dataSeeder = scope.ServiceProvider.GetRequiredService<DataSeeder>();
                if (!hasData || !options.Value.SkipMigrationIfDataExists)
                {
                    seedPlan = await dataSeeder.CreateSeedPlan(cancellationToken);
                    int initialCount = options.Value.InitialSeedCount;

                    logger.LogInformation("Phase 1: Seeding initial {Count} items per type", initialCount);
                    await dataSeeder.SeedItems(seedPlan.Movies.Take(initialCount), cancellationToken);
                    await dataSeeder.SeedItems(seedPlan.Series.Take(initialCount), cancellationToken);
                    await dataSeeder.SeedItems(seedPlan.Sets.Take(initialCount), cancellationToken);
                }

                await dataSeeder.SeedUsers(cancellationToken);
                await dataSeeder.SeedApiKeys(cancellationToken);
            }

            logger.LogInformation("Phase 1 complete — marking healthy");
            seedingHealthCheck.MarkReady();

            if (seedPlan != null)
            {
                using var bgScope = serviceProvider.CreateScope();
                var bgOptions = bgScope.ServiceProvider.GetRequiredService<IOptions<DatabaseMigrationOptions>>();
                int initialCount = bgOptions.Value.InitialSeedCount;
                var bgSeeder = bgScope.ServiceProvider.GetRequiredService<DataSeeder>();

                var remaining = seedPlan.Movies.Skip(initialCount)
                    .Concat(seedPlan.Series.Skip(initialCount))
                    .Concat(seedPlan.Sets.Skip(initialCount))
                    .ToList();

                if (remaining.Count > 0)
                {
                    logger.LogInformation("Phase 2: Seeding remaining {Count} items in background", remaining.Count);
                    await bgSeeder.SeedItems(remaining, cancellationToken);
                    logger.LogInformation("Phase 2 complete — all items imported");
                }
            }

            // Sync local image files to blob storage (non-blocking — web app is already running)
            {
                using var syncScope = serviceProvider.CreateScope();
                var blobSync = syncScope.ServiceProvider.GetRequiredService<BlobSyncService>();
                await blobSync.SyncAsync(cancellationToken);
            }
        }
        catch (Exception ex)
        {
            activity?.AddException(ex);
            throw;
        }

        hostApplicationLifetime.StopApplication();
    }

    private static async Task RunMigrationAsync(SqlServerDataContext dbContext, CancellationToken cancellationToken)
    {
        var strategy = dbContext.Database.CreateExecutionStrategy();
        await strategy.ExecuteAsync(async () =>
        {
            await dbContext.Database.MigrateAsync(cancellationToken);
        });
    }
}