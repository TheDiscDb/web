using System.Diagnostics;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using TheDiscDb.Data.Import;
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
            var dataImporter = scope.ServiceProvider.GetRequiredService<DataImporter>();
            var options = scope.ServiceProvider.GetRequiredService<IOptions<DatabaseMigrationOptions>>();
            
            await RunMigrationAsync(dbContext, cancellationToken);
            await SeedDataAsync(dataImporter, options.Value, cancellationToken);
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

    private async Task SeedDataAsync(DataImporter dataImporter, DatabaseMigrationOptions options, CancellationToken cancellationToken)
    {
        var randomMovies = GetRandomSubdirectories(Path.Combine(options.DataDirectoryRoot, "movie"));
        foreach (var item in randomMovies)
        {
            await dataImporter.Import(item, cancellationToken);
        }

        var randomSeries = GetRandomSubdirectories(Path.Combine(options.DataDirectoryRoot, "series"));
        foreach (var item in randomSeries)
        {
            await dataImporter.Import(item, cancellationToken);
        }

        var randomSets = GetRandomSubdirectories(Path.Combine(options.DataDirectoryRoot, "sets"));
        foreach (var item in randomSets)
        {
            await dataImporter.Import(item, cancellationToken);
        }
    }

    private static IEnumerable<string> GetRandomSubdirectories(string directory, int max = 5)
    {
        var subDirectories = Directory.GetDirectories(directory);
        var randomized = subDirectories.OrderBy(i => Guid.NewGuid()).Take(max);
        return randomized;
    }
}