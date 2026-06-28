namespace TheDiscDb.Services.EditSuggestions;

using System.Threading;
using System.Threading.Tasks;
using TheDiscDb.Web.Data;

/// <summary>
/// Sends email notifications for the edit-suggestion lifecycle, mirroring
/// <see cref="TheDiscDb.Services.IContributionNotificationService"/>. All sends
/// are best-effort: implementations must never throw, so a mail failure can't
/// break the submit/review transaction.
/// </summary>
public interface IEditSuggestionNotificationService
{
    /// <summary>
    /// A user submitted a new suggestion: notify admins that there is something
    /// to review and send the suggester a confirmation.
    /// </summary>
    Task NotifySuggestionSubmittedAsync(
        EditSuggestion suggestion,
        string? userEmail,
        string? userName,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// A suggestion reached a final state (Approved / Rejected /
    /// PartiallyApproved). Sends the suggester a single summary email describing
    /// the outcome of each change. Should NOT be called for non-final states
    /// (e.g. Conflicted), which are handled by the admin in the review UI.
    /// </summary>
    Task NotifySuggestionResolvedAsync(
        EditSuggestion suggestion,
        string? userEmail,
        CancellationToken cancellationToken = default);

    /// <summary>The suggester posted a message: notify the admins.</summary>
    Task NotifyMessageFromUserAsync(
        EditSuggestion suggestion,
        string message,
        string? userName,
        string? userEmail,
        CancellationToken cancellationToken = default);

    /// <summary>An admin posted a message: notify the suggester.</summary>
    Task NotifyMessageFromAdminAsync(
        EditSuggestion suggestion,
        string message,
        string? userEmail,
        CancellationToken cancellationToken = default);
}
