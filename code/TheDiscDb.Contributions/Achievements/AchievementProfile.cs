namespace TheDiscDb.Services.Achievements;

using System;
using System.Collections.Generic;

/// <summary>An achievement a user has earned, paired with its catalog definition.</summary>
public sealed record EarnedAchievement(AchievementDefinition Definition, DateTimeOffset EarnedAtUtc);

/// <summary>Progress toward an un-earned, count-based achievement.</summary>
public sealed record AchievementProgressItem(AchievementDefinition Definition, int Current, int Target)
{
    public int Percent => Target <= 0 ? 0 : (int)Math.Min(100, Math.Round(100.0 * Current / Target));
}

/// <summary>
/// Everything the public profile needs to render a contributor's achievements: their level,
/// total points, earned badges, and progress toward the next few un-earned ones.
/// </summary>
public sealed record UserAchievementProfile(
    string? Username,
    string Level,
    int TotalPoints,
    IReadOnlyList<EarnedAchievement> Earned,
    IReadOnlyList<AchievementProgressItem> InProgress)
{
    public static UserAchievementProfile Empty(string? username) =>
        new(username, LevelCalculator.Newcomer, 0, Array.Empty<EarnedAchievement>(), Array.Empty<AchievementProgressItem>());
}
