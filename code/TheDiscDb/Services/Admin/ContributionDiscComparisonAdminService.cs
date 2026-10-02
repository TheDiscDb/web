using Microsoft.EntityFrameworkCore;
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
    private readonly SqlServerDataContext database;

    public ContributionDiscComparisonAdminService(SqlServerDataContext database)
    {
        this.database = database;
    }

    public async Task<IReadOnlyList<ContributionDiscComparisonListItem>> GetRecentComparisonsAsync(
        int take = 100,
        CancellationToken cancellationToken = default)
    {
        int count = Math.Clamp(take, 1, 500);
        return await this.Query()
            .OrderByDescending(item => item.ComparedAt)
            .ThenByDescending(item => item.Id)
            .Take(count)
            .ToListAsync(cancellationToken);
    }

    public async Task<ContributionDiscParityDashboard> GetDashboardAsync(
        string? producerVersion = null,
        CancellationToken cancellationToken = default)
    {
        var latest = await this.GetLatestComparisonsAsync(cancellationToken);
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
        => await this.Query()
            .Where(item => item.DiscId == discId)
            .OrderByDescending(item => item.ComparedAt)
            .ThenByDescending(item => item.Id)
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyDictionary<int, ContributionDiscComparisonListItem>> GetLatestComparisonsForDiscsAsync(
        IReadOnlyCollection<int> discIds,
        CancellationToken cancellationToken = default)
    {
        if (discIds.Count == 0)
        {
            return new Dictionary<int, ContributionDiscComparisonListItem>();
        }

        var comparisons = await this.Query()
            .Where(item => discIds.Contains(item.DiscId))
            .ToListAsync(cancellationToken);

        return comparisons
            .GroupBy(item => item.DiscId)
            .Select(group => group
                .OrderByDescending(item => item.ComparedAt)
                .ThenByDescending(item => item.Id)
                .First())
            .ToDictionary(item => item.DiscId);
    }

    private async Task<IReadOnlyList<ContributionDiscComparisonListItem>> GetLatestComparisonsAsync(CancellationToken cancellationToken)
    {
        var comparisons = await this.Query().ToListAsync(cancellationToken);
        return comparisons
            .GroupBy(item => item.DiscId)
            .Select(group => group
                .OrderByDescending(item => item.ComparedAt)
                .ThenByDescending(item => item.Id)
                .First())
            .OrderByDescending(item => item.ComparedAt)
            .ThenByDescending(item => item.Id)
            .ToList();
    }

    private IQueryable<ContributionDiscComparisonListItem> Query()
        => this.database.UserContributionDiscComparisons
            .AsNoTracking()
            .Select(comparison => new ContributionDiscComparisonListItem(
                comparison.Id,
                comparison.DiscId,
                comparison.Disc.UserContribution.Id,
                comparison.Disc.UserContribution.ReleaseTitle,
                comparison.Disc.Index,
                comparison.Disc.Name,
                comparison.ComparedAt,
                comparison.Format,
                comparison.ProducerName,
                comparison.ProducerVersion,
                comparison.MakeMkvVersion,
                comparison.UserAgent,
                comparison.LogTitleCount,
                comparison.ManifestTitleCount,
                comparison.MatchedTitleCount,
                comparison.OrderMatches,
                comparison.Status,
                comparison.DifferencesJson));
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
    UserContributionDiscComparisonStatus Status,
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
            latest.Where(item => item.Status != UserContributionDiscComparisonStatus.Match).ToList(),
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
            if (item.Status == UserContributionDiscComparisonStatus.Match)
            {
                matches++;
            }
        }

        return new ContributionDiscParityStats(total, matches);
    }

    private static string NormalizeLabel(string? value)
        => string.IsNullOrWhiteSpace(value) ? "Unknown" : value.Trim();
}
