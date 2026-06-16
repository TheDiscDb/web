namespace TheDiscDb.Data.Changes;

using System.Threading;
using System.Threading.Tasks;
using TheDiscDb.Web.Data;

/// <summary>
/// A single proposed mutation to existing data. Concrete implementations wrap a
/// strongly-typed <c>*Details</c> payload (the proposed state) and know how to
/// (a) detect snapshot drift against the current database and (b) apply themselves.
/// </summary>
public interface IChange
{
    /// <summary>
    /// The stable identifier used to round-trip this change through the
    /// <see cref="EditSuggestionChange.Type"/> column and the <see cref="IChangeFactory"/>.
    /// Convention: dotted lowercase, e.g. <c>release.fields.update</c>.
    /// </summary>
    string TypeKey { get; }

    /// <summary>
    /// The natural-key path for the target entity this change proposes to modify,
    /// derived from the proposed <c>*Details</c> payload. Used when creating
    /// <see cref="EditSuggestion.TargetEntityKey"/> at submission time.
    /// </summary>
    string TargetEntityKey { get; }

    /// <summary>
    /// Re-reads the current state of the target entity from <paramref name="context"/>
    /// and compares it against <paramref name="originalSnapshotJson"/> (the state the
    /// user saw when they submitted the suggestion). Returns a conflict if the database
    /// has drifted from the snapshot; otherwise returns success.
    /// </summary>
    /// <param name="originalSnapshotJson">
    /// The serialised snapshot captured at edit time. Required for changes that
    /// modify existing entities (<see cref="ChangeBase{TDetails}.RequiresOriginalSnapshot"/>
    /// returns <c>true</c>); pure "add" changes may pass <c>null</c>.
    /// </param>
    Task<ChangeValidationResult> ValidateAsync(
        SqlServerDataContext context,
        string? originalSnapshotJson,
        CancellationToken cancellationToken);

    /// <summary>
    /// Applies the proposed mutation to the database. Implementations MUST
    /// re-validate snapshot drift inside this method (using
    /// <see cref="IChangeApplyContext.OriginalSnapshotJson"/>) to close the
    /// time-of-check / time-of-use window between
    /// <see cref="ValidateAsync"/> and <see cref="ApplyAsync"/>. Throws
    /// <see cref="ChangeApplyConflictException"/> if drift is detected at apply time.
    /// Callers are responsible for persisting changes (this method does not call
    /// <c>SaveChangesAsync</c>) and SHOULD wrap the call in a transaction.
    /// </summary>
    Task ApplyAsync(
        SqlServerDataContext context,
        IChangeApplyContext apply,
        CancellationToken cancellationToken);

    /// <summary>
    /// Loads the current state of the target entity from the database and serialises
    /// it as JSON. Used by conflict resolution to take a fresh snapshot as the new
    /// baseline (the admin has accepted the current state). Returns <c>null</c> if
    /// the target entity no longer exists.
    /// </summary>
    Task<string?> GetCurrentSnapshotJsonAsync(
        SqlServerDataContext context,
        CancellationToken cancellationToken);
}
