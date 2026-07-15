namespace TheDiscDb.Services.Achievements;

/// <summary>
/// Derives a level name from a total point count. Levels are auto-derived only — never
/// stored as truth — and any cached value on the user is recomputed from this.
/// </summary>
public static class LevelCalculator
{
    public const string Newcomer = "Newcomer";
    public const string Contributor = "Contributor";
    public const string Archivist = "Archivist";
    public const string TopContributor = "Top Contributor";
    public const string Curator = "Curator";

    public static string ForPoints(int totalPoints) => totalPoints switch
    {
        >= AchievementThresholds.LevelCurator => Curator,
        >= AchievementThresholds.LevelTopContributor => TopContributor,
        >= AchievementThresholds.LevelArchivist => Archivist,
        >= AchievementThresholds.LevelContributor => Contributor,
        _ => Newcomer
    };
}
