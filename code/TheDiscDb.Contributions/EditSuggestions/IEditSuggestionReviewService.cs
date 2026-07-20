namespace TheDiscDb.Services.EditSuggestions;

using System.Threading;
using System.Threading.Tasks;
using TheDiscDb.Web.Data;

/// <summary>
/// Admin-facing operations: approve/reject individual changes within a bundle,
/// approve all remaining, and handle conflict resolution. Each state transition
/// updates the bundle-level status roll-up and writes audit history.
/// </summary>
public interface IEditSuggestionReviewService
{
    /// <summary>
    /// Validates and applies the change, transitioning it to <see cref="EditSuggestionChangeStatus.Applied"/>.
    /// If validation detects drift, the change is marked <see cref="EditSuggestionChangeStatus.Conflicted"/> instead.
    /// Returns the updated change, or null if the change/suggestion was not found.
    /// </summary>
    Task<EditSuggestionChange?> ApproveChangeAsync(
        int suggestionId,
        int changeId,
        string adminUserId,
        string? adminNote = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Marks a change as <see cref="EditSuggestionChangeStatus.Rejected"/>.
    /// </summary>
    Task<EditSuggestionChange?> RejectChangeAsync(
        int suggestionId,
        int changeId,
        string adminUserId,
        string? adminNote = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Approves all remaining <see cref="EditSuggestionChangeStatus.Pending"/> changes in the bundle.
    /// Changes that fail validation are marked <see cref="EditSuggestionChangeStatus.Conflicted"/>.
    /// Returns the suggestion with refreshed status.
    /// </summary>
    Task<EditSuggestion?> ApproveAllPendingAsync(
        int suggestionId,
        string adminUserId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Re-validates a conflicted change after an admin has resolved the underlying issue.
    /// If the conflict is cleared, the change is applied. The resolution describes
    /// how the admin addressed the conflict (e.g. "accepted current DB state as baseline").
    /// </summary>
    Task<EditSuggestionChange?> ResolveConflictAsync(
        int suggestionId,
        int changeId,
        string adminUserId,
        string resolution,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Builds the context for resolving a conflicted <c>disc.fields.update</c> Disc ID change:
    /// the submitted id, the target disc's current id, and the release-discs sharing the same
    /// content hash (candidate destinations). Returns null when the change isn't a Disc ID
    /// conflict.
    /// </summary>
    Task<DiscIdConflictContext?> GetDiscIdConflictContextAsync(
        int suggestionId,
        int changeId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Resolves a Disc ID conflict by attributing the submitted Disc ID to a specific release-disc
    /// (the correct edition/pressing — often a re-pressed sibling that currently has no id). The
    /// conflicted change is retargeted to that release-disc and applied, so it also syncs to
    /// <c>/data</c>. Returns the updated change; if the destination still can't accept the id
    /// (already has a different one, or the id is taken elsewhere) the change stays
    /// <see cref="EditSuggestionChangeStatus.Conflicted"/> with the reason.
    /// </summary>
    Task<EditSuggestionChange?> AttributeDiscIdAsync(
        int suggestionId,
        int changeId,
        int destinationReleaseDiscId,
        string adminUserId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Resolves a Disc ID conflict by <b>swapping</b>: the submitted id is assigned to the conflict
    /// target (the release-disc that already carries an id), and that target's displaced id is moved
    /// to a chosen empty sibling. Use when the original assignment was mis-attributed — the new id is
    /// the target's true id and the old id really belongs to the sibling. Overwrites the target's id
    /// (the only resolution that does), and records both reassignments as applied changes so they
    /// sync to <c>/data</c>. Returns the updated (primary) change; stays
    /// <see cref="EditSuggestionChangeStatus.Conflicted"/> with a reason if the swap isn't valid
    /// (sibling occupied, is a boxset, different disc, or the submitted id is taken elsewhere).
    /// </summary>
    Task<EditSuggestionChange?> SwapDiscIdAsync(
        int suggestionId,
        int changeId,
        int secondaryReleaseDiscId,
        string adminUserId,
        CancellationToken cancellationToken = default);
}
