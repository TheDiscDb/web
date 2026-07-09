namespace TheDiscDb.Data.Changes;

using System;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using TheDiscDb.Web.Data;

/// <summary>
/// Common base class for <see cref="IChange"/> implementations that mutate a
/// single entity. Centralises the snapshot-drift validation pipeline and the
/// "re-validate inside apply" pattern that closes the TOCTOU window between
/// <see cref="ValidateAsync"/> and <see cref="ApplyAsync"/>.
/// </summary>
/// <remarks>
/// Subclasses provide:
/// <list type="bullet">
///   <item><description><see cref="LoadCurrentSnapshotAsync"/> — read current state as <typeparamref name="TDetails"/></description></item>
///   <item><description><see cref="DescribeDrift"/> — per-field comparison returning a human-readable conflict reason or <c>null</c></description></item>
///   <item><description><see cref="ApplyCoreAsync"/> — load tracked entity and write proposed values</description></item>
///   <item><description><see cref="MissingTargetMessage"/> — message when the target entity has been deleted</description></item>
/// </list>
/// "Add" change types (those without a prior entity) should override
/// <see cref="RequiresOriginalSnapshot"/> to return <c>false</c>.
/// </remarks>
public abstract class ChangeBase<TDetails> : IChange
    where TDetails : class
{
    private readonly JsonSerializerOptions jsonOptions;

    protected ChangeBase(TDetails proposed, JsonSerializerOptions? jsonOptions = null)
    {
        this.Proposed = proposed ?? throw new ArgumentNullException(nameof(proposed));
        this.jsonOptions = jsonOptions ?? ChangeBuilder<TDetails>.DefaultJsonOptions;
    }

    /// <summary>The proposed payload supplied by the user.</summary>
    protected TDetails Proposed { get; }

    public abstract string TypeKey { get; }

    /// <inheritdoc />
    public abstract string TargetEntityKey { get; }

    /// <summary>
    /// When <c>true</c> (the default), <see cref="ValidateAsync"/> requires the
    /// caller to supply <paramref name="originalSnapshotJson"/> and rejects the
    /// change as a conflict if it is missing. Override to <c>false</c> for pure
    /// "add" change types.
    /// </summary>
    public virtual bool RequiresOriginalSnapshot => true;

    /// <summary>
    /// When <c>true</c> (the default), a proposed payload that exactly matches the
    /// current database state is reported as a no-op and skipped at apply time.
    /// Removal ("delete") change types override this to <c>false</c>: for a delete,
    /// "the current state matches the snapshot" means the target still exists exactly
    /// as expected and the removal SHOULD proceed — treating it as a no-op would
    /// silently skip the deletion.
    /// </summary>
    protected virtual bool MatchingSnapshotIsNoOp => true;

    public async Task<ChangeValidationResult> ValidateAsync(
        SqlServerDataContext context,
        string? originalSnapshotJson,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);

        var current = await this.LoadCurrentSnapshotAsync(context, cancellationToken);
        if (current is null)
        {
            return ChangeValidationResult.Conflict(this.MissingTargetMessage());
        }

        if (this.RequiresOriginalSnapshot)
        {
            if (string.IsNullOrWhiteSpace(originalSnapshotJson))
            {
                return ChangeValidationResult.Conflict(
                    $"Original snapshot is required for change type '{this.TypeKey}' but was not supplied.");
            }

            TDetails? original;
            try
            {
                original = JsonSerializer.Deserialize<TDetails>(originalSnapshotJson, this.jsonOptions);
            }
            catch (JsonException ex)
            {
                return ChangeValidationResult.Conflict($"Original snapshot could not be deserialised: {ex.Message}");
            }

            if (original is null)
            {
                return ChangeValidationResult.Conflict("Original snapshot deserialised to null.");
            }

            var drift = this.DescribeDrift(original, current);
            if (drift is not null)
            {
                return ChangeValidationResult.Conflict(drift);
            }

            var additional = await this.ValidateAdditionalAsync(context, original, current, cancellationToken);
            if (additional is not null)
            {
                return additional;
            }
        }
        else
        {
            var additional = await this.ValidateAdditionalAsync(context, null, current, cancellationToken);
            if (additional is not null)
            {
                return additional;
            }
        }

        return Equals(current, this.Proposed) && this.MatchingSnapshotIsNoOp
            ? ChangeValidationResult.NoOp()
            : ChangeValidationResult.Ok();
    }

    public async Task ApplyAsync(
        SqlServerDataContext context,
        IChangeApplyContext apply,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(apply);

        // Re-validate inside the apply call against the originally validated snapshot.
        // This closes the time-of-check / time-of-use window between Validate and Apply:
        // any concurrent write that drifted the entity will be caught here as a
        // ChangeApplyConflictException rather than silently overwriting newer data.
        var revalidation = await this.ValidateAsync(context, apply.OriginalSnapshotJson, cancellationToken);
        if (revalidation.IsConflict)
        {
            throw new ChangeApplyConflictException(
                this.TypeKey,
                revalidation.ConflictReason ?? "Snapshot drift detected at apply time.");
        }

        if (revalidation.IsNoOp)
        {
            // Nothing to do — current state already matches proposed.
            return;
        }

        // Deserialise the original snapshot once and pass it to ApplyCoreAsync so
        // subclasses can compute a snapshot→proposed diff and only write the fields
        // the user actually changed. This is what prevents "form submitted with
        // unrelated fields left at default null" from clobbering existing data.
        var original = DeserialiseSnapshotOrNull(apply.OriginalSnapshotJson);
        await this.ApplyCoreAsync(context, apply, original, cancellationToken);
    }

    /// <summary>
    /// Helper for snapshot-diff updates: assigns <paramref name="proposedValue"/> to the
    /// target field via <paramref name="setter"/> ONLY when it differs from
    /// <paramref name="originalValue"/>. When <paramref name="originalValue"/> is the
    /// default (i.e. <paramref name="original"/> was null — no snapshot available),
    /// always writes the proposed value.
    /// </summary>
    /// <remarks>
    /// Diff semantics: a field is only mutated when the user's snapshot of it disagrees
    /// with their proposed value. This means a payload where the user left fields at
    /// their snapshot values (the common Blazor-form case) writes nothing for those
    /// fields, while an explicit clear (snapshot non-null, proposed null) IS written.
    /// </remarks>
    protected static void SetIfChanged<T>(TDetails? original, T originalValue, T proposedValue, Action<T> setter)
    {
        if (original is null)
        {
            // No snapshot (e.g. "add"-type change): apply the proposed value unconditionally.
            setter(proposedValue);
            return;
        }

        if (!Equals(originalValue, proposedValue))
        {
            setter(proposedValue);
        }
    }

    /// <summary>
    /// Helper for <see cref="DescribeDrift"/> implementations: appends
    /// <paramref name="fieldName"/> to <paramref name="sb"/> when the snapshot
    /// and current values differ. Skips fields that haven't drifted so the
    /// resulting message lists only what actually changed.
    /// </summary>
    protected static void AppendIfDifferent<T>(StringBuilder sb, string fieldName, T originalValue, T currentValue)
    {
        if (!Equals(originalValue, currentValue))
        {
            sb.Append(fieldName).Append(", ");
        }
    }

    private TDetails? DeserialiseSnapshotOrNull(string? snapshotJson)
    {
        if (string.IsNullOrWhiteSpace(snapshotJson))
        {
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize<TDetails>(snapshotJson, this.jsonOptions);
        }
        catch (JsonException)
        {
            // Validate path already returned Conflict for malformed snapshots; if we
            // somehow get here with one, fall back to "no snapshot available" rather
            // than throwing — subclasses' SetIfChanged calls will degrade to writing
            // every field, matching pre-diff behaviour.
            return null;
        }
    }

    /// <summary>
    /// Reads the current state of the target entity from the database and projects
    /// it onto the <typeparamref name="TDetails"/> shape. Return <c>null</c> if the
    /// entity no longer exists. Implementations should NOT use
    /// <c>AsNoTracking</c> — keeping the entity in the change tracker means
    /// <see cref="ApplyCoreAsync"/>'s subsequent lookup is a tracker hit, not a
    /// second SQL roundtrip against potentially-newer data.
    /// </summary>
    protected abstract Task<TDetails?> LoadCurrentSnapshotAsync(
        SqlServerDataContext context,
        CancellationToken cancellationToken);

    /// <summary>
    /// Returns <c>null</c> when <paramref name="original"/> and <paramref name="current"/>
    /// agree on every tracked field, or a human-readable description of the drifted
    /// fields otherwise.
    /// </summary>
    protected abstract string? DescribeDrift(TDetails original, TDetails current);

    /// <summary>
    /// Optional extra validation beyond snapshot-drift — e.g. uniqueness constraints that would
    /// otherwise fail at <c>SaveChanges</c> with an opaque database exception. Return a
    /// <see cref="ChangeValidationResult"/> (typically a conflict) to reject the change with a clear
    /// reason, or <c>null</c> to allow it. <paramref name="original"/> is the user's snapshot (null
    /// for add-type changes); <paramref name="current"/> is the current database state.
    /// </summary>
    protected virtual Task<ChangeValidationResult?> ValidateAdditionalAsync(
        SqlServerDataContext context,
        TDetails? original,
        TDetails current,
        CancellationToken cancellationToken)
        => Task.FromResult<ChangeValidationResult?>(null);

    /// <summary>
    /// Loads the target entity as a tracked instance and writes the diff between
    /// <paramref name="original"/> and <see cref="Proposed"/>. Use
    /// <see cref="SetIfChanged"/> to write only those fields the user actually
    /// changed; this avoids the "form left field at default null clobbers existing
    /// data" footgun. <paramref name="original"/> is <c>null</c> only for "add"-type
    /// changes (those that override <see cref="RequiresOriginalSnapshot"/> to
    /// <c>false</c>) — in that case <see cref="SetIfChanged"/> writes everything.
    /// Do not call <c>SaveChangesAsync</c>; the caller is responsible for persistence.
    /// </summary>
    protected abstract Task ApplyCoreAsync(
        SqlServerDataContext context,
        IChangeApplyContext apply,
        TDetails? original,
        CancellationToken cancellationToken);

    /// <summary>Conflict message returned when the target entity no longer exists.</summary>
    protected abstract string MissingTargetMessage();

    /// <inheritdoc />
    public async Task<string?> GetCurrentSnapshotJsonAsync(
        SqlServerDataContext context,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        var current = await this.LoadCurrentSnapshotAsync(context, cancellationToken);
        if (current is null)
        {
            return null;
        }

        return JsonSerializer.Serialize(current, this.jsonOptions);
    }
}
