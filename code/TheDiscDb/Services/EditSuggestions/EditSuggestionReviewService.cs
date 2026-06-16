namespace TheDiscDb.Services.EditSuggestions;

using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using TheDiscDb.Data.Changes;
using TheDiscDb.Web.Data;

public sealed class EditSuggestionReviewService(
    SqlServerDataContext database,
    IChangeFactory changeFactory,
    IEditSuggestionHistoryService historyService) : IEditSuggestionReviewService
{
    public async Task<EditSuggestionChange?> ApproveChangeAsync(
        int suggestionId,
        int changeId,
        string adminUserId,
        string? adminNote,
        CancellationToken cancellationToken)
    {
        var (suggestion, change) = await LoadChangeAsync(suggestionId, changeId, cancellationToken);
        if (suggestion is null || change is null)
        {
            return null;
        }

        if (change.Status is not EditSuggestionChangeStatus.Pending)
        {
            return null;
        }

        // Materialise the IChange and validate against current DB state.
        var changeInstance = changeFactory.Create(change.Type, change.ProposedJson);
        var validation = await changeInstance.ValidateAsync(database, change.OriginalSnapshotJson, cancellationToken);

        var oldStatus = change.Status;

        if (validation.IsConflict)
        {
            change.Status = EditSuggestionChangeStatus.Conflicted;
            change.ConflictReason = validation.ConflictReason;
            change.AdminNote = adminNote;
            await database.SaveChangesAsync(cancellationToken);

            await historyService.RecordChangeStatusChangedAsync(
                suggestionId, changeId, adminUserId, oldStatus, change.Status, adminNote, cancellationToken);

            await RefreshBundleStatusAsync(suggestion, adminUserId, cancellationToken);
            return change;
        }

        // Apply the mutation (does NOT call SaveChangesAsync itself).
        var applyContext = new ChangeApplyContext(adminUserId, suggestionId, changeId, change.OriginalSnapshotJson);
        await changeInstance.ApplyAsync(database, applyContext, cancellationToken);

        change.Status = EditSuggestionChangeStatus.Applied;
        change.AppliedAt = DateTimeOffset.UtcNow;
        change.AppliedByUserId = adminUserId;
        change.AdminNote = adminNote;
        await database.SaveChangesAsync(cancellationToken);

        await historyService.RecordChangeStatusChangedAsync(
            suggestionId, changeId, adminUserId, oldStatus, change.Status, adminNote, cancellationToken);

        await RefreshBundleStatusAsync(suggestion, adminUserId, cancellationToken);
        return change;
    }

    public async Task<EditSuggestionChange?> RejectChangeAsync(
        int suggestionId,
        int changeId,
        string adminUserId,
        string? adminNote,
        CancellationToken cancellationToken)
    {
        var (suggestion, change) = await LoadChangeAsync(suggestionId, changeId, cancellationToken);
        if (suggestion is null || change is null)
        {
            return null;
        }

        if (change.Status is not EditSuggestionChangeStatus.Pending)
        {
            return null;
        }

        var oldStatus = change.Status;
        change.Status = EditSuggestionChangeStatus.Rejected;
        change.AdminNote = adminNote;
        await database.SaveChangesAsync(cancellationToken);

        await historyService.RecordChangeStatusChangedAsync(
            suggestionId, changeId, adminUserId, oldStatus, change.Status, adminNote, cancellationToken);

        await RefreshBundleStatusAsync(suggestion, adminUserId, cancellationToken);
        return change;
    }

    public async Task<EditSuggestion?> ApproveAllPendingAsync(
        int suggestionId,
        string adminUserId,
        CancellationToken cancellationToken)
    {
        var suggestion = await database.EditSuggestions
            .Include(s => s.Changes.OrderBy(c => c.Ordinal))
            .FirstOrDefaultAsync(s => s.Id == suggestionId, cancellationToken);

        if (suggestion is null)
        {
            return null;
        }

        foreach (var change in suggestion.Changes.Where(c => c.Status == EditSuggestionChangeStatus.Pending))
        {
            await ApproveChangeAsync(suggestionId, change.Id, adminUserId, adminNote: null, cancellationToken);
        }

        // Reload to get fresh statuses after the loop.
        return await database.EditSuggestions
            .Include(s => s.Changes.OrderBy(c => c.Ordinal))
            .FirstOrDefaultAsync(s => s.Id == suggestionId, cancellationToken);
    }

    public async Task<EditSuggestionChange?> ResolveConflictAsync(
        int suggestionId,
        int changeId,
        string adminUserId,
        string resolution,
        CancellationToken cancellationToken)
    {
        var (suggestion, change) = await LoadChangeAsync(suggestionId, changeId, cancellationToken);
        if (suggestion is null || change is null)
        {
            return null;
        }

        if (change.Status is not EditSuggestionChangeStatus.Conflicted)
        {
            return null;
        }

        // Reset to Pending and re-attempt approval with the current DB as the new baseline.
        // The admin has acknowledged the drift by providing a resolution note.
        change.Status = EditSuggestionChangeStatus.Pending;
        change.ConflictReason = null;
        change.AdminNote = $"Conflict resolved: {resolution}";

        // Take a fresh snapshot of the current DB state to use as the new "original".
        // This ensures drift detection passes (current == snapshot we just took) and
        // ApplyCoreAsync gets the correct baseline for delta computation.
        var changeInstance = changeFactory.Create(change.Type, change.ProposedJson);
        var freshSnapshotJson = await changeInstance.GetCurrentSnapshotJsonAsync(database, cancellationToken);

        if (freshSnapshotJson is null)
        {
            // Target entity no longer exists — still conflicted.
            change.Status = EditSuggestionChangeStatus.Conflicted;
            change.ConflictReason = "Target entity no longer exists in the database.";
            await database.SaveChangesAsync(cancellationToken);
            return change;
        }

        var validation = await changeInstance.ValidateAsync(database, freshSnapshotJson, cancellationToken);

        if (validation.IsConflict)
        {
            // Still can't resolve (unexpected — snapshot is fresh).
            change.Status = EditSuggestionChangeStatus.Conflicted;
            change.ConflictReason = validation.ConflictReason;
            await database.SaveChangesAsync(cancellationToken);
            return change;
        }

        // Apply with the fresh snapshot as the baseline.
        var applyContext = new ChangeApplyContext(adminUserId, suggestionId, changeId, freshSnapshotJson);
        await changeInstance.ApplyAsync(database, applyContext, cancellationToken);

        change.Status = EditSuggestionChangeStatus.Applied;
        change.AppliedAt = DateTimeOffset.UtcNow;
        change.AppliedByUserId = adminUserId;
        await database.SaveChangesAsync(cancellationToken);

        await historyService.RecordChangeStatusChangedAsync(
            suggestionId, changeId, adminUserId,
            EditSuggestionChangeStatus.Conflicted, EditSuggestionChangeStatus.Applied,
            change.AdminNote, cancellationToken);

        await RefreshBundleStatusAsync(suggestion, adminUserId, cancellationToken);
        return change;
    }

    private async Task<(EditSuggestion? Suggestion, EditSuggestionChange? Change)> LoadChangeAsync(
        int suggestionId,
        int changeId,
        CancellationToken cancellationToken)
    {
        var suggestion = await database.EditSuggestions
            .Include(s => s.Changes)
            .FirstOrDefaultAsync(s => s.Id == suggestionId, cancellationToken);

        var change = suggestion?.Changes.FirstOrDefault(c => c.Id == changeId);
        return (suggestion, change);
    }

    /// <summary>
    /// Recomputes the bundle-level <see cref="EditSuggestion.Status"/> from the
    /// individual change statuses. The roll-up rules:
    /// - All Applied → Approved
    /// - All Rejected → Rejected
    /// - Any Conflicted → Conflicted
    /// - Mix of Applied + Rejected (none pending/conflicted) → PartiallyApproved
    /// - Any still Pending → InReview (once at least one non-pending exists)
    /// </summary>
    private async Task RefreshBundleStatusAsync(
        EditSuggestion suggestion,
        string adminUserId,
        CancellationToken cancellationToken)
    {
        var changes = suggestion.Changes;
        var oldStatus = suggestion.Status;

        EditSuggestionStatus newStatus;

        if (changes.All(c => c.Status == EditSuggestionChangeStatus.Applied))
        {
            newStatus = EditSuggestionStatus.Approved;
        }
        else if (changes.All(c => c.Status == EditSuggestionChangeStatus.Rejected))
        {
            newStatus = EditSuggestionStatus.Rejected;
        }
        else if (changes.Any(c => c.Status == EditSuggestionChangeStatus.Conflicted))
        {
            newStatus = EditSuggestionStatus.Conflicted;
        }
        else if (changes.Any(c => c.Status == EditSuggestionChangeStatus.Pending))
        {
            newStatus = EditSuggestionStatus.InReview;
        }
        else
        {
            // Mix of Applied + Rejected with none pending/conflicted.
            newStatus = EditSuggestionStatus.PartiallyApproved;
        }

        if (oldStatus != newStatus)
        {
            suggestion.Status = newStatus;

            if (newStatus is EditSuggestionStatus.Approved or EditSuggestionStatus.Rejected or EditSuggestionStatus.PartiallyApproved)
            {
                suggestion.ReviewedByUserId = adminUserId;
                suggestion.ReviewedAt = DateTimeOffset.UtcNow;
            }

            await database.SaveChangesAsync(cancellationToken);
            await historyService.RecordStatusChangedAsync(
                suggestion.Id, adminUserId, oldStatus, newStatus, cancellationToken);
        }
    }

    private sealed record ChangeApplyContext(
        string ApprovingUserId,
        int SuggestionId,
        int ChangeId,
        string? OriginalSnapshotJson) : IChangeApplyContext;
}
