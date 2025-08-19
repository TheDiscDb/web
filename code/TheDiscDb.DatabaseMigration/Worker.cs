using System.Diagnostics;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using TheDiscDb.Web.Data;

namespace TheDiscDb.DatabaseMigration;

public class Worker(
    IServiceProvider serviceProvider,
    IHostApplicationLifetime hostApplicationLifetime) : BackgroundService
{
    public const string ActivitySourceName = "Migrations";
    private static readonly ActivitySource activitySource = new(ActivitySourceName);

    protected override async Task ExecuteAsync(CancellationToken cancellationToken)
    {
        using var activity = activitySource.StartActivity("Migrating database", ActivityKind.Client);

        try
        {
            using var scope = serviceProvider.CreateScope();
            
            var dbContext = scope.ServiceProvider.GetRequiredService<SqlServerDataContext>();
            var options = scope.ServiceProvider.GetRequiredService<IOptions<DatabaseMigrationOptions>>();
            
            await RunMigrationAsync(dbContext, cancellationToken);

            bool hasData = await dbContext.MediaItems.AnyAsync();
            if (!hasData || !options.Value.SkipMigrationIfDataExists)
            {
                var dataSeeder = scope.ServiceProvider.GetRequiredService<DataSeeder>();
                await dataSeeder.SeedDataAsync(cancellationToken);
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
            // Run migration in a transaction to avoid partial migration if it fails.
            await dbContext.Database.MigrateAsync(cancellationToken);
        });
    }
}