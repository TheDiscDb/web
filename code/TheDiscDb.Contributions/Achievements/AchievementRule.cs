namespace TheDiscDb.Services.Achievements;

/// <summary>
/// The outcome of evaluating an <see cref="AchievementDefinition"/> rule against a
/// <see cref="ContributorStats"/> snapshot. <see cref="Current"/>/<see cref="Target"/>
/// drive progress display for count-based achievements; for one-time achievements
/// <see cref="Target"/> is 1.
/// </summary>
public readonly record struct AchievementProgressResult(bool Earned, int Current, int Target)
{
    /// <summary>Convenience factory for a simple count-based rule.</summary>
    public static AchievementProgressResult FromCount(int current, int target)
        => new(current >= target, current, target);
}

/// <summary>
/// Pure rule that maps a contributor's stats snapshot to an earned/progress result.
/// Rules must be side-effect free so the event path, nightly reconciliation, and launch
/// backfill can all share one evaluation and remain idempotent.
/// </summary>
public delegate AchievementProgressResult AchievementRule(ContributorStats stats);
