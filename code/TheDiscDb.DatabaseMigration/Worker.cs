using System.Diagnostics;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Sqids;
using TheDiscDb.Data.Import;
using TheDiscDb.ImportModels;
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

            await TrySeedContribution(dbContext, scope.ServiceProvider.GetRequiredService<IStaticAssetStore>(), options, cancellationToken);
        }
        catch (Exception ex)
        {
            activity?.AddException(ex);
            throw;
        }

        hostApplicationLifetime.StopApplication();
    }

    private async Task TrySeedContribution(SqlServerDataContext dbContext, IStaticAssetStore assetStore, IOptions<DatabaseMigrationOptions> options, CancellationToken cancellationToken)
    {
        var hasContributions = await dbContext.UserContributions.AnyAsync(cancellationToken);
        if (!hasContributions && !string.IsNullOrWhiteSpace(options.Value.DataDirectoryRoot))
        {
            string title = "Escape from L.A (1996)";
            string releaseSlug = "2022-4k";

            //C:\code\thediscdb\data\data\movie\Escape from L.A (1996)
            string titleRoot = Path.Combine(options.Value.DataDirectoryRoot, "movie", title);
            var metadataPath = Path.Combine(titleRoot, "metadata.json");
            var releasePath = Path.Combine(titleRoot, releaseSlug, "release.json");
            var discPath = Path.Combine(titleRoot, releaseSlug, "disc01.json");

            if (File.Exists(metadataPath) && File.Exists(releasePath))
            {
                var metadataJson = await File.ReadAllTextAsync(metadataPath, cancellationToken);
                var releaseJson = await File.ReadAllTextAsync(releasePath, cancellationToken);
                var discJson = await File.ReadAllTextAsync(discPath, cancellationToken);

                var metadata = System.Text.Json.JsonSerializer.Deserialize<MetadataFile>(metadataJson);
                var release = System.Text.Json.JsonSerializer.Deserialize<ReleaseFile>(releaseJson);
                var disc = System.Text.Json.JsonSerializer.Deserialize<DiscFile>(discJson);

                if (metadata != null && release != null)
                {
                    var contribution = new UserContribution
                    {
                        UserId = "system",
                        MediaType = "movie",
                        Asin = release.Asin,
                        BackImageUrl = "https://m.media-amazon.com/images/I/81HU16y1qeL._SL1500_.jpg",
                        FrontImageUrl = "https://m.media-amazon.com/images/I/81mK2-zAO9L._SL1500_.jpg",
                        Created = DateTimeOffset.UtcNow,
                        ExternalId = metadata.ExternalIds.Tmdb,
                        ExternalProvider = "tmdb",
                        ReleaseDate = release.ReleaseDate,
                        ReleaseSlug = release.Slug,
                        ReleaseTitle = release.Title,
                        Status = UserContributionStatus.Pending,
                        Upc = release.Upc,
                        Discs = new List<UserContributionDisc>
                        {
                            new UserContributionDisc
                            {
                                ContentHash = disc.ContentHash,
                                Format = disc.Format,
                                Index = disc.Index,
                                Name = disc.Name,
                                Slug = disc.Slug,
                                LogsUploaded = true
                            }
                        }
                    };

                    dbContext.UserContributions.Add(contribution);
                    await dbContext.SaveChangesAsync(cancellationToken);

                    var idEncoder = new SqidsEncoder<int>();

                    var discLogPath = Path.Combine(titleRoot, releaseSlug, "disc01.txt");
                    string contributionId = idEncoder.Encode(contribution.Id);
                    string discId = idEncoder.Encode(contribution.Discs.First().Id);
                    await assetStore.Save(discLogPath, $"{contributionId}/{discId}-logs.txt", ContentTypes.TextContentType, cancellationToken);
                }
            }
        }
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