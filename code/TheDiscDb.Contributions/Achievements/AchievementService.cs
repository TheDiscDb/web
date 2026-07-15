namespace TheDiscDb.Services.Achievements;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using TheDiscDb.Web.Data;

/// <inheritdoc />
public sealed class AchievementService(
    IDbContextFactory<SqlServerDataContext> dbContextFactory,
    IContributorStatsBuilder statsBuilder,
    ILogger<AchievementService> logger) : IAchievementService
{
    public async Task<AchievementEvaluationResult> EvaluateUserAsync(
        string userId, AchievementAuditActor actor, CancellationToken cancellationToken = default)
    {
        var stats = await statsBuilder.BuildAsync(userId, cancellationToken);

        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);

        var earnedRows = await db.UserAchievements
            .Where(a => a.UserId == userId)
            .ToListAsync(cancellationToken);
        var earnedByKey = earnedRows.ToDictionary(a => a.AchievementKey);

        var progressRows = await db.UserAchievementProgress
            .Where(p => p.UserId == userId)
            .ToListAsync(cancellationToken);
        var progressByKey = progressRows.ToDictionary(p => p.AchievementKey);

        var now = DateTimeOffset.UtcNow;
        int newlyAwarded = 0;
        int revoked = 0;
        var earnedKeys = new HashSet<string>(earnedByKey.Keys);

        foreach (var def in AchievementRegistry.Grantable)
        {
            var result = def.Evaluate(stats);

            if (result.Earned)
            {
                if (!earnedByKey.ContainsKey(def.Key))
                {
                    db.UserAchievements.Add(new UserAchievement
                    {
                        UserId = userId,
                        ContributorName = stats.ContributorName,
                        AchievementKey = def.Key,
                        EarnedAtUtc = now
                    });
                    db.AchievementAuditEntries.Add(NewAudit(userId, def.Key, AchievementAuditAction.Awarded, actor, now,
                        $"Earned '{def.Name}' ({result.Current}/{result.Target})."));
                    earnedKeys.Add(def.Key);
                    newlyAwarded++;
                }

                // Earned achievements no longer need a progress row.
                if (progressByKey.TryGetValue(def.Key, out var stale))
                {
                    db.UserAchievementProgress.Remove(stale);
                }
            }
            else
            {
                // Cosmetic activity badges reflect transient state: revoke when no longer true.
                if (def.IsActivityOnly && earnedByKey.TryGetValue(def.Key, out var existing))
                {
                    db.UserAchievements.Remove(existing);
                    db.AchievementAuditEntries.Add(NewAudit(userId, def.Key, AchievementAuditAction.Revoked, actor, now,
                        $"Activity badge '{def.Name}' no longer applies."));
                    earnedKeys.Remove(def.Key);
                    revoked++;
                }
                else if (!def.IsActivityOnly && result.Target > 1)
                {
                    UpsertProgress(db, progressByKey, userId, def.Key, result.Current, result.Target, now);
                }
            }
        }

        int totalPoints = AchievementRegistry.All
            .Where(d => !d.IsActivityOnly && earnedKeys.Contains(d.Key))
            .Sum(d => d.Points);
        var level = LevelCalculator.ForPoints(totalPoints);

        var user = await db.Users.FirstOrDefaultAsync(u => u.Id == userId, cancellationToken);
        if (user is not null)
        {
            user.TotalPoints = totalPoints;
            user.Level = level;
        }

        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException ex)
        {
            // A concurrent evaluation (event + nightly) may have inserted the same earned row.
            // Awarding is idempotent, so a unique-index clash is benign — log and move on.
            logger.LogWarning(ex, "Achievement evaluation for {UserId} hit a concurrent update; treating as idempotent.", userId);
        }

        return new AchievementEvaluationResult(newlyAwarded, revoked, totalPoints, level);
    }

    public async Task<UserAchievementProfile> GetProfileAsync(string username, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(username))
        {
            return UserAchievementProfile.Empty(username);
        }

        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);

        var normalized = username.ToUpperInvariant();
        var user = await db.Users
            .Where(u => u.NormalizedUserName == normalized)
            .Select(u => new { u.Id, u.UserName, u.TotalPoints, u.Level })
            .FirstOrDefaultAsync(cancellationToken);

        if (user is null)
        {
            return UserAchievementProfile.Empty(username);
        }

        var earnedRows = await db.UserAchievements
            .Where(a => a.UserId == user.Id)
            .Select(a => new { a.AchievementKey, a.EarnedAtUtc })
            .ToListAsync(cancellationToken);

        var progressRows = await db.UserAchievementProgress
            .Where(p => p.UserId == user.Id)
            .Select(p => new { p.AchievementKey, p.Current, p.Target })
            .ToListAsync(cancellationToken);

        var earned = earnedRows
            .Select(a => (Def: AchievementRegistry.Find(a.AchievementKey), a.EarnedAtUtc))
            .Where(x => x.Def is not null)
            .Select(x => new EarnedAchievement(x.Def!, x.EarnedAtUtc))
            .OrderBy(e => e.Definition.Category)
            .ThenBy(e => e.Definition.Order)
            .ToList();

        var inProgress = progressRows
            .Select(p => (Def: AchievementRegistry.Find(p.AchievementKey), p.Current, p.Target))
            .Where(x => x.Def is not null && !x.Def!.IsRetired)
            .Select(x => new AchievementProgressItem(x.Def!, x.Current, x.Target))
            .OrderByDescending(p => p.Percent)
            .ThenBy(p => p.Definition.Category)
            .ThenBy(p => p.Definition.Order)
            .ToList();

        var level = string.IsNullOrEmpty(user.Level) ? LevelCalculator.ForPoints(user.TotalPoints) : user.Level;
        return new UserAchievementProfile(user.UserName, level, user.TotalPoints, earned, inProgress);
    }

    private static void UpsertProgress(
        SqlServerDataContext db,
        IReadOnlyDictionary<string, UserAchievementProgress> progressByKey,
        string userId, string key, int current, int target, DateTimeOffset now)
    {
        if (progressByKey.TryGetValue(key, out var row))
        {
            row.Current = current;
            row.Target = target;
            row.UpdatedAtUtc = now;
        }
        else
        {
            db.UserAchievementProgress.Add(new UserAchievementProgress
            {
                UserId = userId,
                AchievementKey = key,
                Current = current,
                Target = target,
                UpdatedAtUtc = now
            });
        }
    }

    private static AchievementAuditEntry NewAudit(
        string userId, string key, AchievementAuditAction action, AchievementAuditActor actor,
        DateTimeOffset now, string reason) => new()
    {
        UserId = userId,
        AchievementKey = key,
        Action = action,
        Actor = actor,
        Reason = reason,
        TimestampUtc = now
    };
}
