namespace TheDiscDb.Services.EditSuggestions;

using TheDiscDb.Web.Data;

/// <summary>
/// Processes selected suggestions sequentially, oldest submission first, with ID as a tie-breaker.
/// Selection and display order do not affect processing order.
/// </summary>
public interface IEditSuggestionBulkReviewService
{
    Task<IReadOnlyList<BulkReviewResult>> ApproveAsync(
        IReadOnlyList<int> suggestionIds,
        string adminUserId,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<BulkReviewResult>> RejectAsync(
        IReadOnlyList<int> suggestionIds,
        string adminUserId,
        string reason,
        CancellationToken cancellationToken = default);
}

public enum BulkReviewOutcome
{
    Approved,
    Rejected,
    NeedsReview,
    Skipped,
    Failed
}

public sealed record BulkReviewCounts(int Applied, int Conflicted, int Rejected, int Pending);

public sealed record BulkReviewResult(
    int SuggestionId,
    string Label,
    BulkReviewOutcome Outcome,
    EditSuggestionStatus? Status,
    BulkReviewCounts? Counts,
    string Message);