namespace TheDiscDb.Services.EditSuggestions;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using TheDiscDb.Web.Data;

public sealed class EditSuggestionBulkReviewService(
    IServiceScopeFactory scopeFactory,
    ILogger<EditSuggestionBulkReviewService> logger) : IEditSuggestionBulkReviewService
{
    public Task<IReadOnlyList<BulkReviewResult>> ApproveAsync(
        IReadOnlyList<int> suggestionIds,
        string adminUserId,
        CancellationToken cancellationToken = default) =>
        ProcessAsync(suggestionIds, adminUserId, rejectionReason: null, cancellationToken);

    public Task<IReadOnlyList<BulkReviewResult>> RejectAsync(
        IReadOnlyList<int> suggestionIds,
        string adminUserId,
        string reason,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(reason);
        return ProcessAsync(suggestionIds, adminUserId, reason.Trim(), cancellationToken);
    }

    private async Task<IReadOnlyList<BulkReviewResult>> ProcessAsync(
        IReadOnlyList<int> suggestionIds,
        string adminUserId,
        string? rejectionReason,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(adminUserId);
        List<BulkReviewResult> results = [];
        cancellationToken.ThrowIfCancellationRequested();
        var ids = suggestionIds.Distinct().ToArray();
        if (ids.Length == 0)
        {
            return results;
        }

        List<int> orderedIds;
        await using (var scope = scopeFactory.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<SqlServerDataContext>();
            orderedIds = await db.EditSuggestions
                .Where(s => ids.Contains(s.Id))
                .OrderBy(s => s.Created)
                .ThenBy(s => s.Id)
                .Select(s => s.Id)
                .ToListAsync(cancellationToken);
        }

        foreach (var id in orderedIds.Concat(ids.Except(orderedIds)))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var label = "Suggestion";
            try
            {
                // A failed apply must not leave tracked mutations in the next item's context.
                await using var scope = scopeFactory.CreateAsyncScope();
                var db = scope.ServiceProvider.GetRequiredService<SqlServerDataContext>();
                var suggestion = await db.EditSuggestions
                    .AsNoTracking()
                    .Include(s => s.Changes)
                    .FirstOrDefaultAsync(s => s.Id == id, cancellationToken);

                if (suggestion is null)
                {
                    results.Add(new(id, label, BulkReviewOutcome.Skipped, null, null, "Suggestion not found."));
                    continue;
                }

                label = suggestion.Summary ?? suggestion.TargetEntityKey ?? "Suggestion";
                if (!suggestion.Status.IsReviewable())
                {
                    results.Add(Result(suggestion, label, BulkReviewOutcome.Skipped,
                        "This suggestion is no longer reviewable."));
                    continue;
                }

                var eligible = rejectionReason is null
                    ? suggestion.Changes.Any(c => c.Status == EditSuggestionChangeStatus.Pending)
                    : suggestion.Changes.Any(c => c.Status is EditSuggestionChangeStatus.Pending
                        or EditSuggestionChangeStatus.Approved
                        or EditSuggestionChangeStatus.Applied
                        or EditSuggestionChangeStatus.Conflicted);
                if (!eligible)
                {
                    results.Add(Result(suggestion, label, BulkReviewOutcome.Skipped,
                        "No changes are eligible for this action."));
                    continue;
                }

                var review = scope.ServiceProvider.GetRequiredService<IEditSuggestionReviewService>();
                var updated = rejectionReason is null
                    ? await review.ApproveAllPendingAsync(id, adminUserId, cancellationToken)
                    : await review.RejectAllChangesAsync(id, adminUserId, rejectionReason, cancellationToken);

                if (updated is null)
                {
                    results.Add(new(id, label, BulkReviewOutcome.Skipped, null, null,
                        "Suggestion not found or no longer eligible."));
                }
                else if (rejectionReason is not null && updated.Status == EditSuggestionStatus.Rejected)
                {
                    results.Add(Result(updated, label, BulkReviewOutcome.Rejected, "Suggestion rejected."));
                }
                else if (rejectionReason is null && updated.Status == EditSuggestionStatus.Approved)
                {
                    results.Add(Result(updated, label, BulkReviewOutcome.Approved, "Pending changes approved and applied."));
                }
                else
                {
                    results.Add(Result(updated, label, BulkReviewOutcome.NeedsReview,
                        "Processing completed, but this suggestion still needs review. Conflicts were not resolved."));
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Bulk review failed for suggestion {SuggestionId}", id);
                results.Add(new(id, label, BulkReviewOutcome.Failed, null, null,
                    "Processing failed. Some changes may already have been saved. Review the details before retrying."));
            }
        }

        return results;
    }

    private static BulkReviewResult Result(
        EditSuggestion suggestion,
        string label,
        BulkReviewOutcome outcome,
        string message) =>
        new(suggestion.Id, label, outcome, suggestion.Status,
            new(
                suggestion.Changes.Count(c => c.Status == EditSuggestionChangeStatus.Applied),
                suggestion.Changes.Count(c => c.Status == EditSuggestionChangeStatus.Conflicted),
                suggestion.Changes.Count(c => c.Status == EditSuggestionChangeStatus.Rejected),
                suggestion.Changes.Count(c => c.Status == EditSuggestionChangeStatus.Pending)),
            message);
}