namespace TheDiscDb.Data.Changes;

/// <summary>
/// Result of <see cref="IChange.ValidateAsync"/>. A successful result means the
/// change can be applied; a conflict result means the target entity has drifted
/// from the snapshot the user submitted against and the change should be flagged
/// for manual admin resolution.
/// </summary>
public sealed class ChangeValidationResult
{
    private ChangeValidationResult(bool isConflict, bool isNoOp, string? conflictReason)
    {
        this.IsConflict = isConflict;
        this.IsNoOp = isNoOp;
        this.ConflictReason = conflictReason;
    }

    public bool IsConflict { get; }

    /// <summary>
    /// True when the proposed payload matches the current database state — i.e. applying
    /// the change would be a no-op. Callers can short-circuit instead of writing.
    /// </summary>
    public bool IsNoOp { get; }

    public string? ConflictReason { get; }

    public static ChangeValidationResult Ok() => new(isConflict: false, isNoOp: false, conflictReason: null);

    public static ChangeValidationResult NoOp() => new(isConflict: false, isNoOp: true, conflictReason: null);

    public static ChangeValidationResult Conflict(string reason)
        => new(isConflict: true, isNoOp: false, conflictReason: reason);
}
