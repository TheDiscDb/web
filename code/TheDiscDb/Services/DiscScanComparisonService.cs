using System.Text;
using System.Reflection;
using System.Security.Cryptography;
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

    Task<DiscRecompareBatchResult> RecompareAllDiscsAsync(CancellationToken cancellationToken = default);
}

public sealed record DiscRecompareFailure(int DiscId, string Error);
public sealed record DiscRecompareBatchResult(
    int Attempted, int Computed, int Persisted, int Skipped, int ErrorRecords,
    IReadOnlyList<DiscRecompareFailure> Failures);

public sealed class DiscScanComparisonService : IDiscScanComparisonService
{
    private static readonly string ComparerVersion = typeof(DiscLogManifestComparer).Assembly
        .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
        ?? typeof(DiscLogManifestComparer).Assembly.GetName().Version?.ToString() ?? "unknown";
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
        var pending = await this.PrepareAsync(discId, cancellationToken);
        return pending is null ? null : await this.SaveAsync(
            pending.ContributionId, pending.DiscId, pending.Record, cancellationToken);
    }

    private async Task<PendingComparison?> PrepareAsync(int discId, CancellationToken cancellationToken)
    {
        await using var dbContext = await this.dbContextFactory.CreateDbContextAsync(cancellationToken);
        var disc = await dbContext.UserContributionDiscs
            .Include(item => item.UserContribution)
            .Include(item => item.Items).ThenInclude(item => item.AudioTracks)
            .Include(item => item.Items).ThenInclude(item => item.SubtitleTracks)
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

        string? referenceHash = null, manifestHash = null;
        DiscScanComparisonRecord record;
        try
        {
            byte[] logBytes = (await this.assetStore.Download(logPath, cancellationToken)).ToArray();
            referenceHash = Convert.ToHexString(SHA256.HashData(logBytes));
            byte[] manifestBytes = (await this.assetStore.Download(manifestPath, cancellationToken)).ToArray();
            manifestHash = Convert.ToHexString(SHA256.HashData(manifestBytes));
            string logText = Encoding.UTF8.GetString(logBytes);
            string manifestJson = Encoding.UTF8.GetString(manifestBytes);

            DiscInfo logDiscInfo = LogParser.Organize(LogParser.Parse(SplitLines(logText)));
            var parsedManifest = new OpticalDiscManifestValidator().Parse(manifestJson);
            if (!parsedManifest.IsValid)
            {
                record = CreateErrorRecord(disc, parsedManifest.Error ?? "The manifest could not be parsed.", referenceHash, manifestHash);
            }
            else
            {
                DiscInfo manifestDiscInfo = OpticalDiscManifestMapper.ToDiscInfo(parsedManifest.Document!);
                var references = disc.Items.Select(item => new UsefulContentReference(
                    item.Name, item.Source, item.SegmentMap, item.Duration,
                    Required: item.Type is "MainMovie" or "Episode",
                    Labels: item.AudioTracks.Select(track => new UsefulStreamLabel("Audio", track.Index, track.Title))
                        .Concat(item.SubtitleTracks.Select(track => new UsefulStreamLabel("Subtitles", track.Index, track.Title))).ToArray())).ToArray();
                var result = DiscLogManifestComparer.Compare(
                    logDiscInfo, logText, parsedManifest.Document!, manifestDiscInfo,
                    string.IsNullOrWhiteSpace(disc.ContentHash) ? null : disc.ContentHash, references);
                record = CreateRecord(disc, result, referenceHash, manifestHash);
            }
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            record = CreateErrorRecord(disc, ex.Message, referenceHash, manifestHash);
        }

        return new(encodedContributionId, encodedDiscId, record);
    }

    public async Task<DiscRecompareBatchResult> RecompareAllDiscsAsync(CancellationToken cancellationToken = default)
    {
        await using var dbContext = await this.dbContextFactory.CreateDbContextAsync(cancellationToken);
        var discIds = await dbContext.UserContributionDiscs
            .Select(disc => disc.Id)
            .ToListAsync(cancellationToken);

        int attempted = 0, computed = 0, persisted = 0, skipped = 0, errors = 0;
        var failures = new List<DiscRecompareFailure>();
        foreach (int discId in discIds)
        {
            cancellationToken.ThrowIfCancellationRequested();
            attempted++;
            try
            {
                var pending = await this.PrepareAsync(discId, cancellationToken);
                if (pending is null)
                {
                    skipped++;
                    continue;
                }
                computed++;
                if (pending.Record.Status == DiscLogManifestComparisonStatus.Error)
                {
                    errors++;
                }
                await this.SaveAsync(pending.ContributionId, pending.DiscId, pending.Record, cancellationToken);
                persisted++;
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex) { failures.Add(new(discId, ex.Message)); }
        }

        return new(attempted, computed, persisted, skipped, errors, failures);
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

    private static DiscScanComparisonRecord CreateRecord(
        UserContributionDisc disc, DiscLogManifestComparisonResult result, string? referenceHash, string? manifestHash)
        => new()
        {
            ComparedAt = DateTimeOffset.UtcNow,
            ComparerVersion = ComparerVersion,
            ReferenceSha256 = referenceHash,
            ManifestSha256 = manifestHash,
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

    private static DiscScanComparisonRecord CreateErrorRecord(
        UserContributionDisc disc, string error, string? referenceHash, string? manifestHash)
        => new()
        {
            ComparedAt = DateTimeOffset.UtcNow,
            ComparerVersion = ComparerVersion,
            ReferenceSha256 = referenceHash,
            ManifestSha256 = manifestHash,
            Status = DiscLogManifestComparisonStatus.Error,
            Format = disc.Format,
            UserAgent = disc.ManifestUserAgent,
            Differences = new DiscLogManifestDifferences
            {
                ExpectedContentHash = string.IsNullOrWhiteSpace(disc.ContentHash) ? null : disc.ContentHash,
                Error = error,
                UsefulContent = new UsefulContentComparison { Status = UsefulContentStatus.NotComparable, Reason = error },
                IdentifyExperience = IdentifyExperienceComparison.NotComparable(error)
            }
        };

    private sealed record PendingComparison(string ContributionId, string DiscId, DiscScanComparisonRecord Record);
}