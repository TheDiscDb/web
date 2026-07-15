namespace TheDiscDb.Web.Data;

using System;

/// <summary>
/// An achievement earned by a user. The achievement itself is defined in code
/// (the achievement registry), not the database — this row only records that a
/// given user has earned the achievement identified by its stable string
/// <see cref="AchievementKey"/>. Awarding is idempotent: a unique index on
/// (<see cref="UserId"/>, <see cref="AchievementKey"/>) guarantees at most one row.
/// </summary>
public class UserAchievement : IHasId
{
    public int Id { get; set; }

    /// <summary>The authenticated account that earned the achievement.</summary>
    public string UserId { get; set; } = string.Empty;

    /// <summary>
    /// Resolved GitHub username / <see cref="TheDiscDb.InputModels.Contributor.Name"/>
    /// captured at award time for display. Denormalised so the public profile can render
    /// without re-resolving the identity bridge.
    /// </summary>
    public string? ContributorName { get; set; }

    /// <summary>Stable slug of the achievement definition in the code registry.</summary>
    public string AchievementKey { get; set; } = string.Empty;

    public DateTimeOffset EarnedAtUtc { get; set; }

    /// <summary>
    /// Optional context describing what triggered a one-time award (e.g. the release or
    /// disc natural key). Null for aggregate/tier achievements.
    /// </summary>
    public string? Context { get; set; }
}
