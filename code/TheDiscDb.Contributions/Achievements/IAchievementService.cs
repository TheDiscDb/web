namespace TheDiscDb.Services.Achievements;

using System.Threading;
using System.Threading.Tasks;
using TheDiscDb.Web.Data;

/// <summary>Summary of an <see cref="IAchievementService.EvaluateUserAsync"/> run.</summary>
public readonly record struct AchievementEvaluationResult(int NewlyAwarded, int Revoked, int TotalPoints, string Level);

/// <summary>
/// Evaluates and awards achievements for a user and exposes their profile for display.
/// Awarding is idempotent ("ensure earned") so the event path, nightly reconciliation, and
/// launch backfill can all call <see cref="EvaluateUserAsync"/> safely and repeatedly.
/// </summary>
public interface IAchievementService
{
    /// <summary>
    /// Recomputes the user's achievements from a fresh stats snapshot: awards any newly-earned
    /// achievements, syncs cosmetic activity badges, refreshes progress, and recomputes the
    /// cached total points / level. Safe to call repeatedly.
    /// </summary>
    Task<AchievementEvaluationResult> EvaluateUserAsync(
        string userId, AchievementAuditActor actor, CancellationToken cancellationToken = default);

    /// <summary>Loads a contributor's achievement profile by GitHub username for display.</summary>
    Task<UserAchievementProfile> GetProfileAsync(string username, CancellationToken cancellationToken = default);
}
