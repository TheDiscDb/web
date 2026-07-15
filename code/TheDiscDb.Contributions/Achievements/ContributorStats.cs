namespace TheDiscDb.Services.Achievements;

using System;

/// <summary>
/// An immutable, per-user snapshot of the facts achievements are computed from. Built once
/// per evaluation by <see cref="IContributorStatsBuilder"/> so every rule is a cheap, pure
/// function of this record. Only quality-gated outcomes are counted (imported contributions,
/// approved edit suggestions, applied Disc IDs); pending/rejected work is excluded, except
/// <see cref="PendingContributionCount"/> which powers cosmetic activity badges only.
/// </summary>
public sealed record ContributorStats
{
    /// <summary>Authenticated account id.</summary>
    public required string UserId { get; init; }

    /// <summary>Resolved GitHub username / Contributor.Name for display, if known.</summary>
    public string? ContributorName { get; init; }

    /// <summary>Published (imported) releases attributed to this contributor.</summary>
    public int PublishedReleaseCount { get; init; }

    /// <summary>Published releases whose media item Type is a series.</summary>
    public int SeriesReleaseCount { get; init; }

    /// <summary>
    /// Distinct box sets the user helped build. Box-set releases don't carry contributor
    /// attribution, so a box set is counted when it shares a (canonical) disc with a
    /// non-box-set release the user contributed.
    /// </summary>
    public int ContributedBoxsetCount { get; init; }

    /// <summary>Approved (or partially-approved) edit suggestions authored by the user.</summary>
    public int ApprovedEditSuggestionCount { get; init; }

    /// <summary>
    /// Applied/approved Disc ID additions: edit-suggestion changes of type
    /// <c>disc.fields.update</c> that set a GlobalDiscId which was not present in the snapshot.
    /// </summary>
    public int DiscIdContributionCount { get; init; }

    /// <summary>Timestamp of the user's first Disc ID contribution, if any.</summary>
    public DateTimeOffset? FirstDiscIdUtc { get; init; }

    /// <summary>Timestamp of the user's first imported contribution, if any.</summary>
    public DateTimeOffset? FirstContributionUtc { get; init; }

    /// <summary>Contributions currently in a pending/in-progress state (cosmetic only).</summary>
    public int PendingContributionCount { get; init; }

    /// <summary>Distinct disc formats (DVD, Blu-ray, 4K UHD…) across the user's releases.</summary>
    public int DistinctFormatCount { get; init; }

    /// <summary>Distinct release decades (by year) across the user's releases.</summary>
    public int DistinctDecadeCount { get; init; }

    /// <summary>Distinct genres across the user's releases.</summary>
    public int DistinctGenreCount { get; init; }

    /// <summary>Largest number of the user's releases sharing a single genre.</summary>
    public int MaxReleasesInSingleGenre { get; init; }

    /// <summary>
    /// True when the user has at least one imported contribution that never went through a
    /// ChangesRequested / Rejected step (a clean first-time approval).
    /// </summary>
    public bool HasFirstTry { get; init; }

    /// <summary>Longest run of consecutive calendar months in which the user published a release.</summary>
    public int MaxConsecutiveContributionMonths { get; init; }

    /// <summary>
    /// True when the user resumed contributing after a gap of at least six months between
    /// consecutive active months.
    /// </summary>
    public bool HadComebackGap { get; init; }
}
