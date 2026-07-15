namespace TheDiscDb.Services.Achievements;

using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using T = AchievementThresholds;

/// <summary>
/// The code-defined achievement catalog. The foundation slice ships a focused starter set;
/// more achievements from the design's full catalog are added here over time. Keys are
/// stable and must never change once shipped (they are persisted on earned rows).
/// </summary>
public static class AchievementRegistry
{
    private static readonly ReadOnlyCollection<AchievementDefinition> definitions =
        new(BuildDefinitions().ToList());

    /// <summary>All achievement definitions, including retired ones.</summary>
    public static IReadOnlyList<AchievementDefinition> All => definitions;

    /// <summary>Definitions that can still be earned (not retired).</summary>
    public static IEnumerable<AchievementDefinition> Grantable => definitions.Where(d => !d.IsRetired);

    private static readonly Dictionary<string, AchievementDefinition> byKey =
        definitions.ToDictionary(d => d.Key);

    public static AchievementDefinition? Find(string key)
        => byKey.TryGetValue(key, out var d) ? d : null;

    private static IEnumerable<AchievementDefinition> BuildDefinitions()
    {
        // --- Milestones -----------------------------------------------------
        yield return new AchievementDefinition
        {
            Key = "first-contribution",
            Name = "First Contribution",
            Description = "Your first release was added to the database.",
            Category = AchievementCategory.Milestone,
            Icon = "trophy",
            Points = T.PointsFirstContribution,
            Order = 0,
            Evaluate = s => new AchievementProgressResult(s.PublishedReleaseCount >= 1, s.PublishedReleaseCount, 1)
        };

        yield return ContributorTier("contributor-bronze", "Contributor", AchievementTier.Bronze,
            T.ContributorBronze, T.PointsContributorBronze, 1);
        yield return ContributorTier("contributor-silver", "Regular Contributor", AchievementTier.Silver,
            T.ContributorSilver, T.PointsContributorSilver, 2);
        yield return ContributorTier("contributor-gold", "Prolific Contributor", AchievementTier.Gold,
            T.ContributorGold, T.PointsContributorGold, 3);

        // --- Quality / edits ------------------------------------------------
        yield return new AchievementDefinition
        {
            Key = "first-suggested-edit",
            Name = "First Suggested Edit",
            Description = "Your first suggested edit was approved.",
            Category = AchievementCategory.Quality,
            Icon = "feather",
            Points = T.PointsFirstSuggestedEdit,
            Order = 0,
            Evaluate = s => new AchievementProgressResult(s.ApprovedEditSuggestionCount >= 1, s.ApprovedEditSuggestionCount, 1)
        };

        // --- Series ---------------------------------------------------------
        yield return new AchievementDefinition
        {
            Key = "series-contributor",
            Name = "Series Contributor",
            Description = "You contributed a TV / series release.",
            Category = AchievementCategory.Series,
            Icon = "tv",
            Points = T.PointsSeriesContributor,
            Order = 0,
            Evaluate = s => new AchievementProgressResult(s.SeriesReleaseCount >= 1, s.SeriesReleaseCount, 1)
        };

        yield return CountTier("series-contributor-silver", "Series Aficionado", AchievementTier.Silver,
            "tv", AchievementCategory.Series, T.SeriesContributorSilver, T.PointsSeriesContributorSilver, 1,
            "Contributed {0} published series releases.", s => s.SeriesReleaseCount);
        yield return CountTier("series-contributor-gold", "Series Connoisseur", AchievementTier.Gold,
            "tv", AchievementCategory.Series, T.SeriesContributorGold, T.PointsSeriesContributorGold, 2,
            "Contributed {0} published series releases.", s => s.SeriesReleaseCount);

        // --- Breadth / variety ---------------------------------------------
        yield return CountTier("format-collector-bronze", "Format Collector", AchievementTier.Bronze,
            "compact-disc", AchievementCategory.Breadth, T.FormatCollectorBronze, T.PointsFormatCollectorBronze, 0,
            "Contributed releases across {0} distinct disc formats.", s => s.DistinctFormatCount);
        yield return CountTier("format-collector-silver", "Format Completionist", AchievementTier.Silver,
            "compact-disc", AchievementCategory.Breadth, T.FormatCollectorSilver, T.PointsFormatCollectorSilver, 1,
            "Contributed releases across {0} distinct disc formats.", s => s.DistinctFormatCount);

        yield return CountTier("decade-spanner-bronze", "Decade Spanner", AchievementTier.Bronze,
            "calendar", AchievementCategory.Breadth, T.DecadeSpannerBronze, T.PointsDecadeSpannerBronze, 2,
            "Contributed releases spanning {0} distinct decades.", s => s.DistinctDecadeCount);
        yield return CountTier("decade-spanner-silver", "Time Traveler", AchievementTier.Silver,
            "calendar", AchievementCategory.Breadth, T.DecadeSpannerSilver, T.PointsDecadeSpannerSilver, 3,
            "Contributed releases spanning {0} distinct decades.", s => s.DistinctDecadeCount);

        yield return CountTier("boxset-builder-bronze", "Boxset Builder", AchievementTier.Bronze,
            "cardboard-box", AchievementCategory.Breadth, T.BoxsetBuilderBronze, T.PointsBoxsetBuilderBronze, 4,
            "Helped build {0} box sets.", s => s.ContributedBoxsetCount);
        yield return CountTier("boxset-builder-silver", "Boxset Baron", AchievementTier.Silver,
            "cardboard-box", AchievementCategory.Breadth, T.BoxsetBuilderSilver, T.PointsBoxsetBuilderSilver, 5,
            "Helped build {0} box sets.", s => s.ContributedBoxsetCount);

        // --- Genre ----------------------------------------------------------
        yield return CountTier("genre-hopper-bronze", "Genre Hopper", AchievementTier.Bronze,
            "price-tag", AchievementCategory.Genre, T.GenreHopperBronze, T.PointsGenreHopperBronze, 0,
            "Contributed releases spanning {0} distinct genres.", s => s.DistinctGenreCount);
        yield return CountTier("genre-hopper-silver", "Genre Globetrotter", AchievementTier.Silver,
            "price-tag", AchievementCategory.Genre, T.GenreHopperSilver, T.PointsGenreHopperSilver, 1,
            "Contributed releases spanning {0} distinct genres.", s => s.DistinctGenreCount);

        yield return new AchievementDefinition
        {
            Key = "genre-specialist",
            Name = "Genre Specialist",
            Description = $"Contributed {T.GenreSpecialist} releases in a single genre.",
            Category = AchievementCategory.Genre,
            Icon = "round-star",
            Points = T.PointsGenreSpecialist,
            Threshold = T.GenreSpecialist,
            Order = 2,
            Evaluate = s => AchievementProgressResult.FromCount(s.MaxReleasesInSingleGenre, T.GenreSpecialist)
        };

        // --- Quality / craft ------------------------------------------------
        yield return new AchievementDefinition
        {
            Key = "first-try",
            Name = "First Try",
            Description = "Had a contribution imported without any requested changes.",
            Category = AchievementCategory.Quality,
            Icon = "on-target",
            Points = T.PointsFirstTry,
            Order = 0,
            Evaluate = s => new AchievementProgressResult(s.HasFirstTry, s.HasFirstTry ? 1 : 0, 1)
        };

        // --- Disc ID --------------------------------------------------------
        yield return new AchievementDefinition
        {
            Key = "first-disc-id",
            Name = "First Disc ID",
            Description = "You added your first Disc ID to a disc in the database.",
            Category = AchievementCategory.DiscId,
            Icon = "compact-disc",
            Points = T.PointsFirstDiscId,
            Order = 0,
            Evaluate = s => new AchievementProgressResult(s.DiscIdContributionCount >= 1, s.DiscIdContributionCount, 1)
        };

        yield return DiscIdTier("disc-id-detective-bronze", AchievementTier.Bronze,
            T.DiscIdDetectiveBronze, T.PointsDiscIdDetectiveBronze, 1);
        yield return DiscIdTier("disc-id-detective-silver", AchievementTier.Silver,
            T.DiscIdDetectiveSilver, T.PointsDiscIdDetectiveSilver, 2);
        yield return DiscIdTier("disc-id-detective-gold", AchievementTier.Gold,
            T.DiscIdDetectiveGold, T.PointsDiscIdDetectiveGold, 3);

        // --- Consistency ----------------------------------------------------
        yield return CountTier("active-streak-bronze", "Active Streak", AchievementTier.Bronze,
            "campfire", AchievementCategory.Consistency, T.ActiveStreakBronze, T.PointsActiveStreakBronze, 0,
            "Contributed in {0} consecutive months.", s => s.MaxConsecutiveContributionMonths);
        yield return CountTier("active-streak-silver", "On Fire", AchievementTier.Silver,
            "campfire", AchievementCategory.Consistency, T.ActiveStreakSilver, T.PointsActiveStreakSilver, 1,
            "Contributed in {0} consecutive months.", s => s.MaxConsecutiveContributionMonths);

        yield return new AchievementDefinition
        {
            Key = "comeback",
            Name = "Comeback",
            Description = "Returned to contribute after a long break.",
            Category = AchievementCategory.Consistency,
            Icon = "return-arrow",
            Points = T.PointsComeback,
            Order = 2,
            Evaluate = s => new AchievementProgressResult(s.HadComebackGap, s.HadComebackGap ? 1 : 0, 1)
        };

        // --- Consistency (cosmetic) ----------------------------------------
        yield return new AchievementDefinition
        {
            Key = "in-the-works",
            Name = "In the Works",
            Description = "You have contributions currently pending review.",
            Category = AchievementCategory.Consistency,
            Icon = "hourglass",
            Points = 0,
            IsActivityOnly = true,
            Order = 3,
            Evaluate = s => new AchievementProgressResult(s.PendingContributionCount >= 1, s.PendingContributionCount, 1)
        };

        // Deferred (not cleanly measurable from the current model, tracked for a later slice):
        //   Studio Explorer   — no studio field on MediaItem.
        //   Complete Record   — "all optional metadata" isn't modelled.
        //   Clean Streak      — needs reliable per-contribution review-outcome ordering.
        //   Season Marathon   — season completeness isn't modelled.
        //   Box Set Binger    — needs series-typed box set + disc-count typing.
        //   First to Add      — no per-title first-adder tracking.
        //   Pioneer           — needs a global first-contributor ranking pass (better in the job).
    }

    private static AchievementDefinition CountTier(
        string key, string name, AchievementTier tier, string icon, AchievementCategory category,
        int threshold, int points, int order, string descriptionFormat, Func<ContributorStats, int> selector) => new()
    {
        Key = key,
        Name = name,
        Description = string.Format(System.Globalization.CultureInfo.InvariantCulture, descriptionFormat, threshold),
        Category = category,
        Icon = icon,
        Points = points,
        Tier = tier,
        Threshold = threshold,
        Order = order,
        Evaluate = s => AchievementProgressResult.FromCount(selector(s), threshold)
    };

    private static AchievementDefinition ContributorTier(
        string key, string name, AchievementTier tier, int threshold, int points, int order) => new()
    {
        Key = key,
        Name = name,
        Description = $"Contributed {threshold} published releases.",
        Category = AchievementCategory.Milestone,
        Icon = "medal",
        Points = points,
        Tier = tier,
        Threshold = threshold,
        Order = order,
        Evaluate = s => AchievementProgressResult.FromCount(s.PublishedReleaseCount, threshold)
    };

    private static AchievementDefinition DiscIdTier(
        string key, AchievementTier tier, int threshold, int points, int order) => new()
    {
        Key = key,
        Name = $"Disc ID Detective ({tier})",
        Description = $"Added {threshold} Disc IDs to discs in the database.",
        Category = AchievementCategory.DiscId,
        Icon = "magnifying-glass",
        Points = points,
        Tier = tier,
        Threshold = threshold,
        Order = order + 10,
        Evaluate = s => AchievementProgressResult.FromCount(s.DiscIdContributionCount, threshold)
    };
}
