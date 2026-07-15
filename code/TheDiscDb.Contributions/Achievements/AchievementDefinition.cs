namespace TheDiscDb.Services.Achievements;

/// <summary>
/// Definition of a single achievement, declared in code (the <see cref="AchievementRegistry"/>).
/// Only earned rows and progress are persisted; the catalog itself lives here so it can be
/// tuned and versioned without a database migration.
/// </summary>
public sealed record AchievementDefinition
{
    /// <summary>Stable slug persisted on <c>UserAchievement.AchievementKey</c>. Never change once shipped.</summary>
    public required string Key { get; init; }

    public required string Name { get; init; }

    public required string Description { get; init; }

    public required AchievementCategory Category { get; init; }

    /// <summary>
    /// Asset key for the badge art (a game-icons.net SVG under <c>wwwroot/badges/</c>),
    /// e.g. <c>"trophy"</c>. Rendered by the BadgeIcon component; tier drives the tint.
    /// </summary>
    public required string Icon { get; init; }

    /// <summary>Points contributed to the level total. Zero for activity-only badges.</summary>
    public int Points { get; init; }

    public AchievementTier Tier { get; init; } = AchievementTier.None;

    /// <summary>Count threshold for count-based achievements; null for pure one-time.</summary>
    public int? Threshold { get; init; }

    /// <summary>Cosmetic badge that reflects transient state and awards no points / no level.</summary>
    public bool IsActivityOnly { get; init; }

    /// <summary>Retired achievements stay awarded for holders but are no longer grantable.</summary>
    public bool IsRetired { get; init; }

    /// <summary>Display ordering within a category (ascending).</summary>
    public int Order { get; init; }

    /// <summary>The pure rule evaluated against a contributor's stats snapshot.</summary>
    public required AchievementRule Evaluate { get; init; }
}
