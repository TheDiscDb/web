namespace TheDiscDb.Services.Achievements;

/// <summary>
/// Placeholder point values, count thresholds, and level cut-offs. All numbers here are
/// provisional and expected to be tuned once real contribution volumes are known — they are
/// deliberately isolated in one file so tuning touches nothing else.
/// </summary>
public static class AchievementThresholds
{
    // Count thresholds -------------------------------------------------------
    public const int ContributorBronze = 5;
    public const int ContributorSilver = 25;
    public const int ContributorGold = 100;

    public const int DiscIdDetectiveBronze = 5;
    public const int DiscIdDetectiveSilver = 25;
    public const int DiscIdDetectiveGold = 100;

    public const int SeriesContributorSilver = 5;
    public const int SeriesContributorGold = 25;

    public const int FormatCollectorBronze = 2;
    public const int FormatCollectorSilver = 3;

    public const int DecadeSpannerBronze = 3;
    public const int DecadeSpannerSilver = 5;

    public const int BoxsetBuilderBronze = 1;
    public const int BoxsetBuilderSilver = 5;

    public const int GenreHopperBronze = 5;
    public const int GenreHopperSilver = 10;

    public const int GenreSpecialist = 10;

    public const int ActiveStreakBronze = 3;
    public const int ActiveStreakSilver = 6;

    // Points -----------------------------------------------------------------
    public const int PointsFirstContribution = 10;
    public const int PointsContributorBronze = 15;
    public const int PointsContributorSilver = 40;
    public const int PointsContributorGold = 100;

    public const int PointsFirstSuggestedEdit = 10;
    public const int PointsSeriesContributor = 20;
    public const int PointsSeriesContributorSilver = 40;
    public const int PointsSeriesContributorGold = 100;

    public const int PointsFirstDiscId = 10;
    public const int PointsDiscIdDetectiveBronze = 15;
    public const int PointsDiscIdDetectiveSilver = 40;
    public const int PointsDiscIdDetectiveGold = 100;

    public const int PointsFormatCollectorBronze = 15;
    public const int PointsFormatCollectorSilver = 30;
    public const int PointsDecadeSpannerBronze = 15;
    public const int PointsDecadeSpannerSilver = 30;
    public const int PointsBoxsetBuilderBronze = 15;
    public const int PointsBoxsetBuilderSilver = 40;
    public const int PointsGenreHopperBronze = 15;
    public const int PointsGenreHopperSilver = 30;
    public const int PointsGenreSpecialist = 25;
    public const int PointsActiveStreakBronze = 15;
    public const int PointsActiveStreakSilver = 40;
    public const int PointsComeback = 10;
    public const int PointsFirstTry = 15;

    // Level cut-offs (min total points to reach the level) -------------------
    public const int LevelNewcomer = 0;
    public const int LevelContributor = 25;
    public const int LevelArchivist = 75;
    public const int LevelTopContributor = 200;
    public const int LevelCurator = 500;
}
