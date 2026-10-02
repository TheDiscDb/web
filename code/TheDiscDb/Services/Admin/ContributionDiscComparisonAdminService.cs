using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;
using TheDiscDb.Contributions.OpticalDiscManifest;
using TheDiscDb.Services.Server;
using TheDiscDb.Web.Data;

namespace TheDiscDb.Services.Admin;

public interface IContributionDiscComparisonAdminService
{
    Task<ContributionDiscParityDashboard> GetDashboardAsync(
        string? producerVersion = null,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<ContributionDiscComparisonListItem>> GetRecentComparisonsAsync(
        int take = 100,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<ContributionDiscComparisonListItem>> GetDiscComparisonsAsync(
        int discId,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyDictionary<int, ContributionDiscComparisonListItem>> GetLatestComparisonsForDiscsAsync(
        IReadOnlyCollection<int> discIds,
        CancellationToken cancellationToken = default);
}

public sealed class ContributionDiscComparisonAdminService : IContributionDiscComparisonAdminService
{
    private static readonly JsonSerializerOptions DifferencesJsonOptions = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private readonly SqlServerDataContext database;
    private readonly DiscScanComparisonStore comparisonStore;
    private readonly IdEncoder idEncoder;

    public ContributionDiscComparisonAdminService(
        SqlServerDataContext database,
        DiscScanComparisonStore comparisonStore,
        IdEncoder idEncoder)
    {
        this.database = database;
        this.comparisonStore = comparisonStore;
        this.idEncoder = idEncoder;
    }

    public async Task<IReadOnlyList<ContributionDiscComparisonListItem>> GetRecentComparisonsAsync(
        int take = 100,
        CancellationToken cancellationToken = default)
    {
        int count = Math.Clamp(take, 1, 500);
        var comparisons = await this.LoadAsync(discIds: null, cancellationToken);
        return comparisons
            .OrderByDescending(item => item.ComparedAt)
            .Take(count)
            .ToList();
    }

    public async Task<ContributionDiscParityDashboard> GetDashboardAsync(
        string? producerVersion = null,
        CancellationToken cancellationToken = default)
    {
        var latest = Latest(await this.LoadAsync(discIds: null, cancellationToken));
        var producerVersions = latest
            .Select(item => item.ProducerVersion)
            .Where(item => !string.IsNullOrWhiteSpace(item))
            .Select(item => item!)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Order(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        if (!string.IsNullOrWhiteSpace(producerVersion))
        {
            latest = latest
                .Where(item => string.Equals(item.ProducerVersion, producerVersion, StringComparison.OrdinalIgnoreCase))
                .ToList();
        }

        return DiscScanParityAggregator.CreateDashboard(latest, producerVersions);
    }

    public async Task<IReadOnlyList<ContributionDiscComparisonListItem>> GetDiscComparisonsAsync(
        int discId,
        CancellationToken cancellationToken = default)
        => (await this.LoadAsync([discId], cancellationToken))
            .OrderByDescending(item => item.ComparedAt)
            .ThenByDescending(item => item.Id)
            .ToList();

    public async Task<IReadOnlyDictionary<int, ContributionDiscComparisonListItem>> GetLatestComparisonsForDiscsAsync(
        IReadOnlyCollection<int> discIds,
        CancellationToken cancellationToken = default)
    {
        if (discIds.Count == 0)
        {
            return new Dictionary<int, ContributionDiscComparisonListItem>();
        }

        return Latest(await this.LoadAsync(discIds, cancellationToken)).ToDictionary(item => item.DiscId);
    }

    private static List<ContributionDiscComparisonListItem> Latest(IEnumerable<ContributionDiscComparisonListItem> comparisons)
        => comparisons
            .GroupBy(item => item.DiscId)
            .Select(group => group
                .OrderByDescending(item => item.ComparedAt)
                .ThenByDescending(item => item.Id)
                .First())
            .OrderByDescending(item => item.ComparedAt)
            .ToList();

    // Comparison history lives in a blob next to each disc's log and scan, so only discs
    // that have both uploads can have one.
    private async Task<List<ContributionDiscComparisonListItem>> LoadAsync(
        IReadOnlyCollection<int>? discIds,
        CancellationToken cancellationToken)
    {
        var query = this.database.UserContributionDiscs
            .AsNoTracking()
            .Where(disc => disc.LogsUploaded && disc.ManifestUploaded);

        if (discIds is not null)
        {
            query = query.Where(disc => discIds.Contains(disc.Id));
        }

        var discs = await query
            .Select(disc => new
            {
                disc.Id,
                ContributionId = disc.UserContribution.Id,
                disc.UserContribution.ReleaseTitle,
                disc.Index,
                disc.Name
            })
            .ToListAsync(cancellationToken);

        var items = new List<ContributionDiscComparisonListItem>();
        foreach (var disc in discs)
        {
            var file = await this.comparisonStore.ReadAsync(
                this.idEncoder.Encode(disc.ContributionId),
                this.idEncoder.Encode(disc.Id),
                cancellationToken);

            if (file is null)
            {
                continue;
            }

            // The file is stored newest first; number runs so the oldest is 1.
            for (int i = 0; i < file.Comparisons.Count; i++)
            {
                var run = file.Comparisons[i];
                items.Add(new ContributionDiscComparisonListItem(
                    file.Comparisons.Count - i,
                    disc.Id,
                    disc.ContributionId,
                    disc.ReleaseTitle,
                    disc.Index,
                    disc.Name,
                    run.ComparedAt,
                    run.Format,
                    run.ProducerName,
                    run.ProducerVersion,
                    run.MakeMkvVersion,
                    run.UserAgent,
                    run.LogTitleCount,
                    run.ManifestTitleCount,
                    run.MatchedTitleCount,
                    run.OrderMatches,
                    run.Status,
                    JsonSerializer.Serialize(run.Differences, DifferencesJsonOptions)));
            }
        }

        return items;
    }
}
public sealed record ContributionDiscComparisonListItem(
    int Id,
    int DiscId,
    int ContributionId,
    string ReleaseTitle,
    int? DiscIndex,
    string DiscName,
    DateTimeOffset ComparedAt,
    string? Format,
    string? ProducerName,
    string? ProducerVersion,
    string? MakeMkvVersion,
    string? UserAgent,
    int LogTitleCount,
    int ManifestTitleCount,
    int MatchedTitleCount,
    bool OrderMatches,
    DiscLogManifestComparisonStatus Status,
    string DifferencesJson);

public sealed record ContributionDiscParityDashboard(
    ContributionDiscParityStats Overall,
    IReadOnlyList<ContributionDiscParityBreakdown> ByFormat,
    IReadOnlyList<ContributionDiscParityBreakdown> ByProducerVersion,
    IReadOnlyList<ContributionDiscParityBreakdown> ByMakeMkvVersion,
    IReadOnlyList<ContributionDiscParityBreakdown> ByBrowser,
    IReadOnlyList<ContributionDiscComparisonListItem> NonMatches,
    IReadOnlyList<string> ProducerVersions);

public sealed record ContributionDiscParityBreakdown(string Label, ContributionDiscParityStats Stats);

public sealed record ContributionDiscParityStats(int Total, int Matches)
{
    public double MatchRate => Total == 0 ? 0 : (double)Matches / Total;
}

public static class DiscScanParityAggregator
{
    public static ContributionDiscParityDashboard CreateDashboard(
        IReadOnlyList<ContributionDiscComparisonListItem> latest,
        IReadOnlyList<string>? producerVersions = null)
        => new(
            CreateStats(latest),
            CreateBreakdown(latest, item => item.Format),
            CreateBreakdown(latest, item => item.ProducerVersion),
            CreateBreakdown(latest, item => item.MakeMkvVersion),
            CreateBreakdown(latest, item => BrowserName(item.UserAgent)),
            latest.Where(item => item.Status != DiscLogManifestComparisonStatus.Match).ToList(),
            producerVersions ?? latest
                .Select(item => item.ProducerVersion)
                .Where(item => !string.IsNullOrWhiteSpace(item))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Order(StringComparer.OrdinalIgnoreCase)
                .ToList()!);

    public static string BrowserName(string? userAgent)
    {
        if (string.IsNullOrWhiteSpace(userAgent))
        {
            return "Other";
        }

        if (userAgent.Contains("DiscScan.Cli", StringComparison.OrdinalIgnoreCase)
            || userAgent.Contains("CLI", StringComparison.OrdinalIgnoreCase))
        {
            return "CLI";
        }

        if (userAgent.Contains("Edg/", StringComparison.OrdinalIgnoreCase)
            || userAgent.Contains("Edge/", StringComparison.OrdinalIgnoreCase))
        {
            return "Edge";
        }

        if (userAgent.Contains("Firefox/", StringComparison.OrdinalIgnoreCase))
        {
            return "Firefox";
        }

        if (userAgent.Contains("Chrome/", StringComparison.OrdinalIgnoreCase)
            || userAgent.Contains("CriOS/", StringComparison.OrdinalIgnoreCase))
        {
            return "Chrome";
        }

        return "Other";
    }

    private static IReadOnlyList<ContributionDiscParityBreakdown> CreateBreakdown(
        IReadOnlyList<ContributionDiscComparisonListItem> latest,
        Func<ContributionDiscComparisonListItem, string?> keySelector)
        => latest
            .GroupBy(item => NormalizeLabel(keySelector(item)), StringComparer.OrdinalIgnoreCase)
            .Select(group => new ContributionDiscParityBreakdown(group.Key, CreateStats(group)))
            .OrderByDescending(item => item.Stats.Total)
            .ThenBy(item => item.Label, StringComparer.OrdinalIgnoreCase)
            .ToList();

    private static ContributionDiscParityStats CreateStats(IEnumerable<ContributionDiscComparisonListItem> items)
    {
        int total = 0;
        int matches = 0;
        foreach (var item in items)
        {
            total++;
            if (item.Status == DiscLogManifestComparisonStatus.Match)
            {
                matches++;
            }
        }

        return new ContributionDiscParityStats(total, matches);
    }

    private static string NormalizeLabel(string? value)
        => string.IsNullOrWhiteSpace(value) ? "Unknown" : value.Trim();
}
