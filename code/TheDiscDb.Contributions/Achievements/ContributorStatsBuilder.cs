namespace TheDiscDb.Services.Achievements;

using System;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using TheDiscDb.Data.Changes.DiscFields;
using TheDiscDb.Web.Data;

/// <summary>
/// Builds a <see cref="ContributorStats"/> snapshot for a user by aggregating the durable
/// user tables and the (rebuildable) attribution join. Only quality-gated outcomes are
/// counted; see <see cref="ContributorStats"/> for the gating rules.
/// </summary>
public sealed class ContributorStatsBuilder(IDbContextFactory<SqlServerDataContext> dbContextFactory)
    : IContributorStatsBuilder
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task<ContributorStats> BuildAsync(string userId, CancellationToken cancellationToken = default)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);

        var userName = await db.Users
            .Where(u => u.Id == userId)
            .Select(u => u.UserName)
            .FirstOrDefaultAsync(cancellationToken);

        bool hasName = !string.IsNullOrEmpty(userName);

        // Public attribution bridge: Contributor.UserId (account) or Contributor.Name (GitHub login).
        var releases = db.Releases
            .Where(r => r.Contributors.Any(c => c.UserId == userId || (hasName && c.Name == userName)));

        int publishedReleaseCount = await releases.CountAsync(cancellationToken);
        int seriesReleaseCount = await releases.CountAsync(
            r => r.MediaItem != null && r.MediaItem.Type != null && r.MediaItem.Type.ToLower() == "series",
            cancellationToken);

        // Box sets don't carry contributor attribution. A box set's discs are canonicalised to the
        // same Disc rows as the member releases, so we count distinct box sets that share a disc
        // with a non-box-set release this user contributed.
        var contributedDiscIds = releases
            .Where(r => r.Boxset == null)
            .SelectMany(r => r.Discs.Select(d => d.DiscId));
        int contributedBoxsetCount = await db.BoxSets.CountAsync(
            bs => bs.Release != null && bs.Release.Discs.Any(d => contributedDiscIds.Contains(d.DiscId)),
            cancellationToken);

        DateTimeOffset? firstContributionUtc = publishedReleaseCount > 0
            ? await releases.MinAsync(r => (DateTimeOffset?)r.DateAdded, cancellationToken)
            : null;

        int approvedEditSuggestionCount = await db.EditSuggestions.CountAsync(
            e => e.UserId == userId
                && (e.Status == EditSuggestionStatus.Approved || e.Status == EditSuggestionStatus.PartiallyApproved),
            cancellationToken);

        int pendingContributionCount = await db.UserContributions.CountAsync(
            c => c.UserId == userId
                && (c.Status == UserContributionStatus.Pending
                    || c.Status == UserContributionStatus.ReadyForReview
                    || c.Status == UserContributionStatus.ChangesRequested),
            cancellationToken);

        var (discIdCount, firstDiscIdUtc) = await CountDiscIdContributionsAsync(db, userId, cancellationToken);

        // Breadth + activity: one projection over the attributed releases.
        var releaseFacets = await releases
            .Select(r => new ReleaseFacet
            {
                Year = r.Year,
                DateAdded = r.DateAdded,
                Genres = r.MediaItem != null ? r.MediaItem.Genres : null,
                Formats = r.Discs.Select(d => d.Disc!.Format).ToList()
            })
            .ToListAsync(cancellationToken);

        int distinctFormatCount = releaseFacets
            .SelectMany(f => f.Formats)
            .Where(f => !string.IsNullOrWhiteSpace(f))
            .Select(f => f!.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Count();

        int distinctDecadeCount = releaseFacets
            .Where(f => f.Year > 0)
            .Select(f => f.Year / 10)
            .Distinct()
            .Count();

        var (distinctGenreCount, maxReleasesInSingleGenre) = SummarizeGenres(releaseFacets);

        var activeMonths = releaseFacets
            .Select(f => (f.DateAdded.Year * 12) + (f.DateAdded.Month - 1))
            .Distinct()
            .OrderBy(m => m)
            .ToList();
        int maxConsecutiveMonths = MaxConsecutiveRun(activeMonths);
        bool hadComebackGap = HasGap(activeMonths, ComebackGapMonths);

        bool hasFirstTry = await HasCleanFirstTryAsync(db, userId, cancellationToken);

        return new ContributorStats
        {
            UserId = userId,
            ContributorName = userName,
            PublishedReleaseCount = publishedReleaseCount,
            SeriesReleaseCount = seriesReleaseCount,
            ContributedBoxsetCount = contributedBoxsetCount,
            ApprovedEditSuggestionCount = approvedEditSuggestionCount,
            DiscIdContributionCount = discIdCount,
            FirstDiscIdUtc = firstDiscIdUtc,
            FirstContributionUtc = firstContributionUtc,
            PendingContributionCount = pendingContributionCount,
            DistinctFormatCount = distinctFormatCount,
            DistinctDecadeCount = distinctDecadeCount,
            DistinctGenreCount = distinctGenreCount,
            MaxReleasesInSingleGenre = maxReleasesInSingleGenre,
            HasFirstTry = hasFirstTry,
            MaxConsecutiveContributionMonths = maxConsecutiveMonths,
            HadComebackGap = hadComebackGap
        };
    }

    /// <summary>Minimum gap (in months) between active months that counts as a "comeback".</summary>
    private const int ComebackGapMonths = 6;

    private sealed class ReleaseFacet
    {
        public int Year { get; init; }
        public DateTimeOffset DateAdded { get; init; }
        public string? Genres { get; init; }
        public List<string?> Formats { get; init; } = new();
    }

    private static (int Distinct, int MaxInOne) SummarizeGenres(IEnumerable<ReleaseFacet> facets)
    {
        var perGenreReleaseCount = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var facet in facets)
        {
            if (string.IsNullOrWhiteSpace(facet.Genres))
            {
                continue;
            }

            var genres = facet.Genres
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Distinct(StringComparer.OrdinalIgnoreCase);

            foreach (var genre in genres)
            {
                perGenreReleaseCount[genre] = perGenreReleaseCount.GetValueOrDefault(genre) + 1;
            }
        }

        return (perGenreReleaseCount.Count, perGenreReleaseCount.Count == 0 ? 0 : perGenreReleaseCount.Values.Max());
    }

    /// <summary>Longest run of consecutive integers in a sorted, de-duplicated sequence.</summary>
    public static int MaxConsecutiveRun(IReadOnlyList<int> sortedDistinct)
    {
        if (sortedDistinct.Count == 0)
        {
            return 0;
        }

        int best = 1;
        int current = 1;
        for (int i = 1; i < sortedDistinct.Count; i++)
        {
            current = sortedDistinct[i] == sortedDistinct[i - 1] + 1 ? current + 1 : 1;
            best = Math.Max(best, current);
        }

        return best;
    }

    /// <summary>True when any two consecutive values differ by at least <paramref name="gap"/>.</summary>
    public static bool HasGap(IReadOnlyList<int> sortedDistinct, int gap)
    {
        for (int i = 1; i < sortedDistinct.Count; i++)
        {
            if (sortedDistinct[i] - sortedDistinct[i - 1] >= gap)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// True when the user has an imported contribution whose history never records a transition
    /// to ChangesRequested or Rejected. The status-change description is a code-controlled format
    /// (<c>"Status changed from **X** to **Y**"</c>), so a substring match on the target status is
    /// deterministic.
    /// </summary>
    private static async Task<bool> HasCleanFirstTryAsync(SqlServerDataContext db, string userId, CancellationToken cancellationToken)
    {
        var importedIds = await db.UserContributions
            .Where(c => c.UserId == userId && c.Status == UserContributionStatus.Imported)
            .Select(c => c.Id)
            .ToListAsync(cancellationToken);

        if (importedIds.Count == 0)
        {
            return false;
        }

        var revisedIds = await db.ContributionHistory
            .Where(h => importedIds.Contains(h.ContributionId)
                && (h.Description.Contains("to **ChangesRequested**") || h.Description.Contains("to **Rejected**")))
            .Select(h => h.ContributionId)
            .Distinct()
            .ToListAsync(cancellationToken);

        return importedIds.Any(id => !revisedIds.Contains(id));
    }

    /// <summary>
    /// Counts the user's Disc ID additions. Disc IDs arrive as <c>disc.fields.update</c>
    /// edit-suggestion changes (auto-approved to Applied for a clean add); we count only those
    /// whose proposed payload actually set a GlobalDiscId, so a format-only edit doesn't count.
    /// </summary>
    private static async Task<(int Count, DateTimeOffset? First)> CountDiscIdContributionsAsync(
        SqlServerDataContext db, string userId, CancellationToken cancellationToken)
    {
        var candidates = await db.EditSuggestionChanges
            .Where(c => c.Type == DiscFieldsUpdate.Key
                && (c.Status == EditSuggestionChangeStatus.Applied || c.Status == EditSuggestionChangeStatus.Approved)
                && c.Suggestion!.UserId == userId)
            .Select(c => new { c.ProposedJson, c.AppliedAt, c.Suggestion!.Created })
            .ToListAsync(cancellationToken);

        int count = 0;
        DateTimeOffset? first = null;

        foreach (var candidate in candidates)
        {
            if (!SetsGlobalDiscId(candidate.ProposedJson))
            {
                continue;
            }

            count++;
            var when = candidate.AppliedAt ?? candidate.Created;
            if (first is null || when < first)
            {
                first = when;
            }
        }

        return (count, first);
    }

    private static bool SetsGlobalDiscId(string proposedJson)
    {
        if (string.IsNullOrWhiteSpace(proposedJson))
        {
            return false;
        }

        try
        {
            var details = JsonSerializer.Deserialize<DiscFieldsDetails>(proposedJson, JsonOptions);
            return !string.IsNullOrWhiteSpace(details?.GlobalDiscId);
        }
        catch (JsonException)
        {
            return false;
        }
    }
}
