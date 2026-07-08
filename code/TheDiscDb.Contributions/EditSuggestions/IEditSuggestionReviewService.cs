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
}
