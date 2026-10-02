using System.Text;
using System.Text.Json;
using MakeMkv;
using Microsoft.EntityFrameworkCore;
using TheDiscDb.Contributions.OpticalDiscManifest;
using TheDiscDb.Data.Import;
using TheDiscDb.Services.Server;
using TheDiscDb.Web.Data;

namespace TheDiscDb.Services;

public interface IDiscScanComparisonService
{
    Task<UserContributionDiscComparison?> RecompareDiscAsync(int discId, CancellationToken cancellationToken = default);

    Task<int> RecompareAllDiscsAsync(CancellationToken cancellationToken = default);
}

public sealed class DiscScanComparisonService : IDiscScanComparisonService
{
    private readonly IDbContextFactory<SqlServerDataContext> dbContextFactory;
    private readonly IStaticAssetStore assetStore;
    private readonly IdEncoder idEncoder;

    public DiscScanComparisonService(
        IDbContextFactory<SqlServerDataContext> dbContextFactory,
        IStaticAssetStore assetStore,
        IdEncoder idEncoder)
    {
        this.dbContextFactory = dbContextFactory;
        this.assetStore = assetStore;
        this.idEncoder = idEncoder;
    }

    public async Task<UserContributionDiscComparison?> RecompareDiscAsync(int discId, CancellationToken cancellationToken = default)
    {
        await using var dbContext = await this.dbContextFactory.CreateDbContextAsync(cancellationToken);
        var disc = await dbContext.UserContributionDiscs
            .Include(item => item.UserContribution)
            .FirstOrDefaultAsync(item => item.Id == discId, cancellationToken);

        if (disc is null)
        {
            return null;
        }

        string encodedContributionId = this.idEncoder.Encode(disc.UserContribution.Id);
        string encodedDiscId = this.idEncoder.Encode(disc.Id);
        string logPath = ContributionDiscAssets.LogsPath(encodedContributionId, encodedDiscId);
        string manifestPath = ContributionDiscAssets.ManifestPath(encodedContributionId, encodedDiscId);

        if (!await this.assetStore.Exists(logPath, cancellationToken)
            || !await this.assetStore.Exists(manifestPath, cancellationToken))
        {
            return null;
        }

        try
        {
            string logText = Encoding.UTF8.GetString((await this.assetStore.Download(logPath, cancellationToken)).ToArray());
            string manifestJson = Encoding.UTF8.GetString((await this.assetStore.Download(manifestPath, cancellationToken)).ToArray());

            DiscInfo logDiscInfo = LogParser.Organize(LogParser.Parse(SplitLines(logText)));
            var parsedManifest = new OpticalDiscManifestValidator().Parse(manifestJson);
            if (!parsedManifest.IsValid)
            {
                return await SaveErrorAsync(dbContext, disc, parsedManifest.Error ?? "The manifest could not be parsed.", cancellationToken);
            }

            DiscInfo manifestDiscInfo = OpticalDiscManifestMapper.ToDiscInfo(parsedManifest.Document!);
            var result = DiscLogManifestComparer.Compare(
                logDiscInfo,
                logText,
                parsedManifest.Document!,
                manifestDiscInfo,
                string.IsNullOrWhiteSpace(disc.ContentHash) ? null : disc.ContentHash);

            return await SaveResultAsync(dbContext, disc, result, cancellationToken);
        }
        catch (Exception ex)
        {
            return await SaveErrorAsync(dbContext, disc, ex.Message, cancellationToken);
        }
    }

    public async Task<int> RecompareAllDiscsAsync(CancellationToken cancellationToken = default)
    {
        await using var dbContext = await this.dbContextFactory.CreateDbContextAsync(cancellationToken);
        var discIds = await dbContext.UserContributionDiscs
            .Select(disc => disc.Id)
            .ToListAsync(cancellationToken);

        int count = 0;
        foreach (int discId in discIds)
        {
            if (await this.RecompareDiscAsync(discId, cancellationToken) is not null)
            {
                count++;
            }
        }

        return count;
    }

    private static string[] SplitLines(string text)
        => text.Split(["\r\n", "\n"], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    private static async Task<UserContributionDiscComparison> SaveResultAsync(
        SqlServerDataContext dbContext,
        UserContributionDisc disc,
        DiscLogManifestComparisonResult result,
        CancellationToken cancellationToken)
    {
        var comparison = new UserContributionDiscComparison
        {
            DiscId = disc.Id,
            ComparedAt = DateTimeOffset.UtcNow,
            Format = result.Format,
            ProducerName = result.ProducerName,
            ProducerVersion = result.ProducerVersion,
            MakeMkvVersion = result.MakeMkvVersion,
            UserAgent = disc.ManifestUserAgent,
            LogTitleCount = result.LogTitleCount,
            ManifestTitleCount = result.ManifestTitleCount,
            MatchedTitleCount = result.MatchedTitleCount,
            OrderMatches = result.OrderMatches,
            Status = Enum.Parse<UserContributionDiscComparisonStatus>(result.Status.ToString()),
            DifferencesJson = result.DifferencesJson
        };

        dbContext.UserContributionDiscComparisons.Add(comparison);
        await dbContext.SaveChangesAsync(cancellationToken);
        return comparison;
    }

    private static async Task<UserContributionDiscComparison> SaveErrorAsync(
        SqlServerDataContext dbContext,
        UserContributionDisc disc,
        string error,
        CancellationToken cancellationToken)
    {
        var differences = new DiscLogManifestDifferences
        {
            ExpectedContentHash = string.IsNullOrWhiteSpace(disc.ContentHash) ? null : disc.ContentHash,
            Error = error
        };

        var comparison = new UserContributionDiscComparison
        {
            DiscId = disc.Id,
            ComparedAt = DateTimeOffset.UtcNow,
            Format = disc.Format,
            UserAgent = disc.ManifestUserAgent,
            OrderMatches = false,
            Status = UserContributionDiscComparisonStatus.Error,
            DifferencesJson = JsonSerializer.Serialize(differences, new JsonSerializerOptions(JsonSerializerDefaults.Web))
        };

        dbContext.UserContributionDiscComparisons.Add(comparison);
        await dbContext.SaveChangesAsync(cancellationToken);
        return comparison;
    }
}
