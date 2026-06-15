namespace TheDiscDb.Data.Changes;

using System;
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

    /// <summary>
    /// When <c>true</c> (the default), <see cref="ValidateAsync"/> requires the
    /// caller to supply <paramref name="originalSnapshotJson"/> and rejects the
    /// change as a conflict if it is missing. Override to <c>false</c> for pure
    /// "add" change types.
    /// </summary>
    public virtual bool RequiresOriginalSnapshot => true;

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
        }

        return Equals(current, this.Proposed)
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

        await this.ApplyCoreAsync(context, apply, cancellationToken);
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
    /// Loads the target entity as a tracked instance and writes the proposed values.
    /// Do not call <c>SaveChangesAsync</c>; the caller is responsible for persistence.
    /// </summary>
    protected abstract Task ApplyCoreAsync(
        SqlServerDataContext context,
        IChangeApplyContext apply,
        CancellationToken cancellationToken);

    /// <summary>Conflict message returned when the target entity no longer exists.</summary>
    protected abstract string MissingTargetMessage();
}
