namespace TheDiscDb.Web.Data;

using System;

/// <summary>What happened to an achievement in an <see cref="AchievementAuditEntry"/>.</summary>
public enum AchievementAuditAction
{
    Awarded,
    Revoked
}

/// <summary>Who caused an <see cref="AchievementAuditEntry"/>.</summary>
public enum AchievementAuditActor
{
    /// <summary>Awarded by the event-driven path during normal contribution/edit flow.</summary>
    System,

    /// <summary>Awarded by the one-time launch backfill.</summary>
    Backfill,

    /// <summary>Awarded by the nightly reconciliation job.</summary>
    Reconciliation,

    /// <summary>Manually granted/revoked by an administrator.</summary>
    Admin
}

/// <summary>
/// Audit record of an achievement award/revoke. Achievements are user-facing state
/// changes, so each grant is logged with actor, reason, and timestamp for transparency
/// and debugging, consistent with the project's auditing practice for such features.
/// </summary>
public class AchievementAuditEntry : IHasId
{
    public int Id { get; set; }

    public string UserId { get; set; } = string.Empty;

    /// <summary>Stable slug of the achievement definition in the code registry.</summary>
    public string AchievementKey { get; set; } = string.Empty;

    public AchievementAuditAction Action { get; set; }

    public AchievementAuditActor Actor { get; set; }

    /// <summary>Optional human-readable reason (e.g. threshold crossed, backfill run).</summary>
    public string? Reason { get; set; }

    public DateTimeOffset TimestampUtc { get; set; }
}
