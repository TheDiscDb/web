namespace TheDiscDb.Data.Changes;

/// <summary>
/// Context provided to <see cref="IChange.ApplyAsync"/> identifying who is applying
/// the change and which suggestion / change row it belongs to. Lets implementations
/// stamp audit fields without taking dependencies on review-service plumbing.
/// </summary>
public interface IChangeApplyContext
{
    /// <summary>The user id of the admin (or trusted user) approving the change.</summary>
    string ApprovingUserId { get; }

    int SuggestionId { get; }

    int ChangeId { get; }

    /// <summary>
    /// The original snapshot JSON the user submitted against, or <c>null</c> for
    /// pure "add" changes. Passed into <see cref="IChange.ApplyAsync"/> so the
    /// change can re-validate drift inside the apply transaction (closing the
    /// TOCTOU window between <see cref="IChange.ValidateAsync"/> and
    /// <see cref="IChange.ApplyAsync"/>).
    /// </summary>
    string? OriginalSnapshotJson { get; }
}
