using Microsoft.EntityFrameworkCore;
using TheDiscDb.Web.Data;

namespace TheDiscDb.Services.Admin;

public interface IContributionDiscComparisonAdminService
{
    Task<IReadOnlyList<ContributionDiscComparisonListItem>> GetRecentComparisonsAsync(
        int take = 100,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<ContributionDiscComparisonListItem>> GetDiscComparisonsAsync(
        int discId,
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

    public async Task<IReadOnlyList<ContributionDiscComparisonListItem>> GetDiscComparisonsAsync(
        int discId,
        CancellationToken cancellationToken = default)
        => await this.Query()
            .Where(item => item.DiscId == discId)
            .OrderByDescending(item => item.ComparedAt)
            .ThenByDescending(item => item.Id)
            .ToListAsync(cancellationToken);

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
