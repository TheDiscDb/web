namespace TheDiscDb.Web.Data;

using System;

/// <summary>
/// Cached progress toward an un-earned, count-based achievement. Lets the public
/// profile (and the future browse page) render progress bars cheaply without
/// recomputing from history on every request. Refreshed on the event path and by
/// the nightly reconciliation job. A unique index on
/// (<see cref="UserId"/>, <see cref="AchievementKey"/>) keeps one row per pair.
/// </summary>
public class UserAchievementProgress : IHasId
{
    public int Id { get; set; }

    public string UserId { get; set; } = string.Empty;

    /// <summary>Stable slug of the achievement definition in the code registry.</summary>
    public string AchievementKey { get; set; } = string.Empty;

    /// <summary>Current count toward the threshold.</summary>
    public int Current { get; set; }

    /// <summary>Threshold required to earn the achievement.</summary>
    public int Target { get; set; }

    public DateTimeOffset UpdatedAtUtc { get; set; }
}
