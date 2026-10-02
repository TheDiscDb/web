using System.Text;
using MakeMkv;
using Microsoft.EntityFrameworkCore;
using TheDiscDb.Contributions.OpticalDiscManifest;
using TheDiscDb.Data.Import;
using TheDiscDb.Services.Server;
using TheDiscDb.Web.Data;

namespace TheDiscDb.Services;

public interface IDiscScanComparisonService
{
    Task<DiscScanComparisonRecord?> RecompareDiscAsync(int discId, CancellationToken cancellationToken = default);

    Task<int> RecompareAllDiscsAsync(CancellationToken cancellationToken = default);
}

public sealed class DiscScanComparisonService : IDiscScanComparisonService
{
    private readonly IDbContextFactory<SqlServerDataContext> dbContextFactory;
    private readonly IStaticAssetStore assetStore;
    private readonly IdEncoder idEncoder;
    private readonly DiscScanComparisonStore comparisonStore;

    public DiscScanComparisonService(
        IDbContextFactory<SqlServerDataContext> dbContextFactory,
        IStaticAssetStore assetStore,
        IdEncoder idEncoder,
        DiscScanComparisonStore comparisonStore)
    {
        this.dbContextFactory = dbContextFactory;
        this.assetStore = assetStore;
        this.idEncoder = idEncoder;
        this.comparisonStore = comparisonStore;
    }

    public async Task<DiscScanComparisonRecord?> RecompareDiscAsync(int discId, CancellationToken cancellationToken = default)
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
                return await this.SaveAsync(encodedContributionId, encodedDiscId, CreateErrorRecord(disc, parsedManifest.Error ?? "The manifest could not be parsed."), cancellationToken);
            }

            DiscInfo manifestDiscInfo = OpticalDiscManifestMapper.ToDiscInfo(parsedManifest.Document!);
            var result = DiscLogManifestComparer.Compare(
                logDiscInfo,
                logText,
                parsedManifest.Document!,
                manifestDiscInfo,
                string.IsNullOrWhiteSpace(disc.ContentHash) ? null : disc.ContentHash);

            return await this.SaveAsync(encodedContributionId, encodedDiscId, CreateRecord(disc, result), cancellationToken);
        }
        catch (Exception ex)
        {
            return await this.SaveAsync(encodedContributionId, encodedDiscId, CreateErrorRecord(disc, ex.Message), cancellationToken);
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

    private async Task<DiscScanComparisonRecord> SaveAsync(
        string encodedContributionId,
        string encodedDiscId,
        DiscScanComparisonRecord record,
        CancellationToken cancellationToken)
    {
        await this.comparisonStore.AppendAsync(encodedContributionId, encodedDiscId, record, cancellationToken);
        return record;
    }

    private static DiscScanComparisonRecord CreateRecord(UserContributionDisc disc, DiscLogManifestComparisonResult result)
        => new()
        {
            ComparedAt = DateTimeOffset.UtcNow,
            Status = result.Status,
            Format = result.Format,
            ProducerName = result.ProducerName,
            ProducerVersion = result.ProducerVersion,
            MakeMkvVersion = result.MakeMkvVersion,
            UserAgent = disc.ManifestUserAgent,
            LogTitleCount = result.LogTitleCount,
            ManifestTitleCount = result.ManifestTitleCount,
            MatchedTitleCount = result.MatchedTitleCount,
            OrderMatches = result.OrderMatches,
            Differences = result.Differences
        };

    private static DiscScanComparisonRecord CreateErrorRecord(UserContributionDisc disc, string error)
        => new()
        {
            ComparedAt = DateTimeOffset.UtcNow,
            Status = DiscLogManifestComparisonStatus.Error,
            Format = disc.Format,
            UserAgent = disc.ManifestUserAgent,
            Differences = new DiscLogManifestDifferences
            {
                ExpectedContentHash = string.IsNullOrWhiteSpace(disc.ContentHash) ? null : disc.ContentHash,
                Error = error
            }
        };
}