namespace TheDiscDb.Services.EditSuggestions;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using TheDiscDb.Data.Changes;
using TheDiscDb.Data.Changes.DiscFields;
using TheDiscDb.InputModels;
using TheDiscDb.Web.Data;

public sealed class EditSuggestionReviewService(
    SqlServerDataContext database,
    IChangeFactory changeFactory,
    IEditSuggestionHistoryService historyService,
    IEditSuggestionNotificationService? notifications = null,
    IEditSuggestionRecipientResolver? recipients = null) : IEditSuggestionReviewService
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task<EditSuggestionChange?> ApproveChangeAsync(
        int suggestionId,
        int changeId,
        string adminUserId,
        string? adminNote,
        CancellationToken cancellationToken)
    {
        var loaded = await LoadChangeAsync(suggestionId, changeId, cancellationToken);
        if (loaded is null)
        {
            return null;
        }

        var (suggestion, change) = loaded;

        if (!suggestion.Status.IsReviewable())
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
        var loaded = await LoadChangeAsync(suggestionId, changeId, cancellationToken);
        if (loaded is null)
        {
            return null;
        }

        var (suggestion, change) = loaded;

        if (!suggestion.Status.IsReviewable())
        {
            return null;
        }

        if (change.Status is not EditSuggestionChangeStatus.Pending
            and not EditSuggestionChangeStatus.Conflicted)
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

        if (!suggestion.Status.IsReviewable())
        {
            return suggestion;
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
        var loaded = await LoadChangeAsync(suggestionId, changeId, cancellationToken);
        if (loaded is null)
        {
            return null;
        }

        var (suggestion, change) = loaded;

        if (!suggestion.Status.IsReviewable())
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

    public async Task<DiscIdConflictContext?> GetDiscIdConflictContextAsync(
        int suggestionId,
        int changeId,
        CancellationToken cancellationToken = default)
    {
        var loaded = await LoadChangeAsync(suggestionId, changeId, cancellationToken);
        if (loaded is null || loaded.Change.Type != DiscFieldsUpdate.Key)
        {
            return null;
        }

        var change = loaded.Change;
        var proposed = JsonSerializer.Deserialize<DiscFieldsDetails>(change.ProposedJson, JsonOptions);
        if (proposed is null || string.IsNullOrEmpty(proposed.GlobalDiscId))
        {
            return null;
        }

        var contentHash = proposed.ContentHash;

        // The target disc's current stored id (what it "already has").
        var targetDisc = await DiscFieldsUpdate.ResolveDiscAsync(database, proposed, cancellationToken);

        // Candidate destinations: item (media-item) release-discs whose canonical disc shares this
        // content hash. Boxset releases are excluded — a boxset's disc is copied from the item
        // release it references (an "existing disc"), so it inherits that pressing's id via the
        // read-time fallback and never independently owns an AACS id. Attributing a distinct id to a
        // boxset release-disc would contradict that "same disc" relationship.
        var candidates = new List<DiscIdConflictCandidate>();
        if (!string.IsNullOrEmpty(contentHash))
        {
            var rows = await database.Set<ReleaseDisc>()
                .AsNoTracking()
                .Where(rd => rd.Disc != null && rd.Disc.ContentHash == contentHash && rd.Release!.Boxset == null)
                .Select(rd => new
                {
                    rd.Id,
                    rd.Slug,
                    rd.Index,
                    rd.GlobalDiscId,
                    ReleaseSlug = rd.Release!.Slug,
                    ReleaseTitle = rd.Release.Title,
                    MediaItemSlug = rd.Release.MediaItem != null ? rd.Release.MediaItem.Slug : null,
                    MediaTitle = rd.Release.MediaItem != null ? rd.Release.MediaItem.Title : null,
                })
                .ToListAsync(cancellationToken);

            candidates.AddRange(rows.Select(r => new DiscIdConflictCandidate(
                r.Id, r.MediaItemSlug, r.ReleaseSlug ?? string.Empty,
                r.Slug, r.Index, r.ReleaseTitle, r.MediaTitle, r.GlobalDiscId)));
        }

        return new DiscIdConflictContext(
            suggestionId,
            changeId,
            proposed.GlobalDiscId,
            contentHash,
            proposed.TargetEntityKey,
            targetDisc?.GlobalDiscId,
            candidates);
    }

    public async Task<EditSuggestionChange?> AttributeDiscIdAsync(
        int suggestionId,
        int changeId,
        int destinationReleaseDiscId,
        string adminUserId,
        CancellationToken cancellationToken = default)
    {
        var loaded = await LoadChangeAsync(suggestionId, changeId, cancellationToken);
        if (loaded is null)
        {
            return null;
        }

        var (suggestion, change) = loaded;
        if (!suggestion.Status.IsReviewable() || change.Type != DiscFieldsUpdate.Key)
        {
            return null;
        }

        var proposed = JsonSerializer.Deserialize<DiscFieldsDetails>(change.ProposedJson, JsonOptions);
        if (proposed is null || string.IsNullOrEmpty(proposed.GlobalDiscId))
        {
            return null;
        }

        var submittedId = proposed.GlobalDiscId;

        // Resolve the chosen destination release-disc and its natural key.
        var destination = await database.Set<ReleaseDisc>()
            .Include(rd => rd.Disc)
            .Include(rd => rd.Release!).ThenInclude(r => r.MediaItem)
            .Include(rd => rd.Release!).ThenInclude(r => r.Boxset)
            .FirstOrDefaultAsync(rd => rd.Id == destinationReleaseDiscId, cancellationToken);

        if (destination?.Disc is null || destination.Release is null)
        {
            return null;
        }

        // Boxset release-discs inherit their disc (and its id) from the item release they were
        // copied from, so they can't independently own an AACS id. Refuse to attribute to one even
        // if a stale/hand-crafted request supplies it (the UI already excludes them as candidates).
        if (destination.Release.Boxset != null)
        {
            change.Status = EditSuggestionChangeStatus.Conflicted;
            change.ConflictReason = $"Destination release-disc belongs to a boxset, which inherits its disc from an item release — refusing to attribute Disc ID {submittedId}.";
            await database.SaveChangesAsync(cancellationToken);
            return change;
        }

        // Safety: only attribute the id to a disc that actually shares the submitted disc's content.
        if (!string.IsNullOrEmpty(proposed.ContentHash)
            && !string.Equals(destination.Disc.ContentHash, proposed.ContentHash, StringComparison.OrdinalIgnoreCase))
        {
            change.Status = EditSuggestionChangeStatus.Conflicted;
            change.ConflictReason = $"Destination release-disc content hash does not match the submitted disc — refusing to attribute Disc ID {submittedId}.";
            await database.SaveChangesAsync(cancellationToken);
            return change;
        }

        var mediaItemSlug = destination.Release.MediaItem?.Slug;
        var releaseSlug = destination.Release.Slug ?? string.Empty;

        // Retarget the conflicted change to the destination release-disc, then apply it there so it
        // also syncs to /data with the correct target. The original release keeps its own id.
        var snapshot = DiscFieldsUpdate.SnapshotFrom(destination, mediaItemSlug, boxsetSlug: null, releaseSlug);
        var retargeted = snapshot with { GlobalDiscId = submittedId };

        var originalTargetKey = proposed.TargetEntityKey;
        change.ProposedJson = JsonSerializer.Serialize(retargeted, JsonOptions);
        change.OriginalSnapshotJson = JsonSerializer.Serialize(snapshot, JsonOptions);

        var oldStatus = change.Status;
        var note = $"Attributed Disc ID {submittedId} to '{retargeted.TargetEntityKey}' (originally submitted for '{originalTargetKey}').";

        var changeInstance = changeFactory.Create(change.Type, change.ProposedJson);
        var validation = await changeInstance.ValidateAsync(database, change.OriginalSnapshotJson, cancellationToken);
        if (validation.IsConflict)
        {
            change.Status = EditSuggestionChangeStatus.Conflicted;
            change.ConflictReason = validation.ConflictReason;
            change.AdminNote = note;
            await database.SaveChangesAsync(cancellationToken);
            await RefreshBundleStatusAsync(suggestion, adminUserId, cancellationToken);
            return change;
        }

        var applyContext = new ChangeApplyContext(adminUserId, suggestionId, changeId, change.OriginalSnapshotJson);
        await changeInstance.ApplyAsync(database, applyContext, cancellationToken);

        change.Status = EditSuggestionChangeStatus.Applied;
        change.ConflictReason = null;
        change.AppliedAt = DateTimeOffset.UtcNow;
        change.AppliedByUserId = adminUserId;
        change.AdminNote = note;
        await database.SaveChangesAsync(cancellationToken);

        await historyService.RecordChangeStatusChangedAsync(
            suggestionId, changeId, adminUserId, oldStatus, change.Status, note, cancellationToken);

        await RefreshBundleStatusAsync(suggestion, adminUserId, cancellationToken);
        return change;
    }

    public async Task<EditSuggestionChange?> SwapDiscIdAsync(
        int suggestionId,
        int changeId,
        int secondaryReleaseDiscId,
        string adminUserId,
        CancellationToken cancellationToken = default)
    {
        var loaded = await LoadChangeAsync(suggestionId, changeId, cancellationToken);
        if (loaded is null)
        {
            return null;
        }

        var (suggestion, change) = loaded;
        if (!suggestion.Status.IsReviewable() || change.Type != DiscFieldsUpdate.Key)
        {
            return null;
        }

        var proposed = JsonSerializer.Deserialize<DiscFieldsDetails>(change.ProposedJson, JsonOptions);
        if (proposed is null || string.IsNullOrEmpty(proposed.GlobalDiscId))
        {
            return null;
        }

        var submittedId = proposed.GlobalDiscId; // the id being added (Y)

        // Primary = the disc the conflicted change targets — the one that already carries an id (X).
        var primary = await DiscFieldsUpdate.ResolveDiscAsync(database, proposed, cancellationToken);
        if (primary?.Disc is null || primary.Release is null)
        {
            return null;
        }

        // Secondary = the sibling chosen to receive the primary's displaced id.
        var secondary = await database.Set<ReleaseDisc>()
            .Include(rd => rd.Disc)
            .Include(rd => rd.Release!).ThenInclude(r => r.MediaItem)
            .Include(rd => rd.Release!).ThenInclude(r => r.Boxset)
            .FirstOrDefaultAsync(rd => rd.Id == secondaryReleaseDiscId, cancellationToken);
        if (secondary?.Disc is null || secondary.Release is null)
        {
            return null;
        }

        async Task<EditSuggestionChange> RejectAsync(string reason)
        {
            change.Status = EditSuggestionChangeStatus.Conflicted;
            change.ConflictReason = reason;
            await database.SaveChangesAsync(cancellationToken);
            return change;
        }

        var displacedId = primary.GlobalDiscId; // the primary's current id (X)

        if (secondary.Id == primary.Id)
        {
            return await RejectAsync("Swap needs a different sibling release-disc to receive the displaced Disc ID.");
        }

        if (string.IsNullOrEmpty(displacedId))
        {
            return await RejectAsync("Swap requires the target disc to already have a Disc ID to displace; it has none — attribute the submitted id instead.");
        }

        if (secondary.Release.Boxset != null)
        {
            return await RejectAsync($"Destination release-disc belongs to a boxset, which inherits its disc from an item release — refusing to move Disc ID {displacedId} to it.");
        }

        if (!string.IsNullOrEmpty(secondary.GlobalDiscId))
        {
            return await RejectAsync($"Destination release-disc already has Disc ID {secondary.GlobalDiscId}; a swap only fills an empty sibling.");
        }

        // Both pressings must be the same physical content (same canonical disc / content hash).
        if (secondary.DiscId != primary.DiscId
            && !string.Equals(secondary.Disc.ContentHash, primary.Disc.ContentHash, StringComparison.OrdinalIgnoreCase))
        {
            return await RejectAsync("Destination release-disc is a different disc (content hash mismatch) — refusing to swap Disc IDs across unrelated discs.");
        }

        // The submitted id must be free (or already on the primary — a no-op). Reject when a THIRD
        // release-disc owns it.
        var submittedOwnerId = await database.ReleaseDiscs
            .Where(rd => rd.GlobalDiscId == submittedId)
            .Select(rd => (int?)rd.Id)
            .FirstOrDefaultAsync(cancellationToken);
        if (submittedOwnerId is not null && submittedOwnerId != primary.Id)
        {
            return await RejectAsync($"Disc ID {submittedId} is already assigned to a different release-disc — cannot swap it onto this pressing.");
        }

        // Capture pre-swap snapshots (drive the /data sync): primary carries X, secondary carries none.
        var primarySnapshot = DiscFieldsUpdate.SnapshotFrom(
            primary, proposed.MediaItemSlug, proposed.BoxsetSlug, proposed.ReleaseSlug);
        var secondaryMediaSlug = secondary.Release.MediaItem?.Slug;
        var secondaryReleaseSlug = secondary.Release.Slug ?? string.Empty;
        var secondarySnapshot = DiscFieldsUpdate.SnapshotFrom(
            secondary, secondaryMediaSlug, boxsetSlug: null, secondaryReleaseSlug);

        // Perform the swap one row at a time so the unique filtered index on GlobalDiscId is never
        // transiently violated: free X off the primary first, land it on the secondary, then add Y.
        primary.GlobalDiscId = null;
        await database.SaveChangesAsync(cancellationToken);
        secondary.GlobalDiscId = displacedId;
        await database.SaveChangesAsync(cancellationToken);
        primary.GlobalDiscId = submittedId;
        await database.SaveChangesAsync(cancellationToken);

        var oldStatus = change.Status;

        // Retarget the conflicted change to the primary as an OVERWRITE X -> Y. The snapshot records
        // X as the expected pre-image, which is what authorises the tools file applier to overwrite
        // (its GlobalDiscId write is otherwise strictly add-only).
        var primaryProposed = primarySnapshot with { GlobalDiscId = submittedId };
        var primaryNote = $"Swapped Disc IDs: assigned {submittedId} to '{primaryProposed.TargetEntityKey}' and moved its previous id {displacedId} to '{secondarySnapshot.TargetEntityKey}'.";
        change.OriginalSnapshotJson = JsonSerializer.Serialize(primarySnapshot, JsonOptions);
        change.ProposedJson = JsonSerializer.Serialize(primaryProposed, JsonOptions);
        change.Status = EditSuggestionChangeStatus.Applied;
        change.ConflictReason = null;
        change.AppliedAt = DateTimeOffset.UtcNow;
        change.AppliedByUserId = adminUserId;
        change.SyncedToFilesAt = null;
        change.AdminNote = primaryNote;

        // Add a sibling change that records the displaced id (X) landing on the secondary — a normal
        // add-only write for its /data file.
        var secondaryProposed = secondarySnapshot with { GlobalDiscId = displacedId };
        var nextOrdinal = suggestion.Changes.Count == 0 ? 0 : suggestion.Changes.Max(c => c.Ordinal) + 1;
        var secondaryChange = new EditSuggestionChange
        {
            SuggestionId = suggestion.Id,
            Suggestion = suggestion,
            Ordinal = nextOrdinal,
            Type = DiscFieldsUpdate.Key,
            OriginalSnapshotJson = JsonSerializer.Serialize(secondarySnapshot, JsonOptions),
            ProposedJson = JsonSerializer.Serialize(secondaryProposed, JsonOptions),
            Status = EditSuggestionChangeStatus.Applied,
            AppliedAt = DateTimeOffset.UtcNow,
            AppliedByUserId = adminUserId,
            AdminNote = $"Received Disc ID {displacedId} displaced from '{primaryProposed.TargetEntityKey}' during a swap resolution.",
        };
        suggestion.Changes.Add(secondaryChange);
        database.EditSuggestionChanges.Add(secondaryChange);

        await database.SaveChangesAsync(cancellationToken);

        await historyService.RecordChangeStatusChangedAsync(
            suggestionId, change.Id, adminUserId, oldStatus, change.Status, primaryNote, cancellationToken);

        await RefreshBundleStatusAsync(suggestion, adminUserId, cancellationToken);
        return change;
    }

    private async Task<LoadedChange?> LoadChangeAsync(
        int suggestionId,
        int changeId,
        CancellationToken cancellationToken)
    {
        var suggestion = await database.EditSuggestions
            .Include(s => s.Changes)
            .FirstOrDefaultAsync(s => s.Id == suggestionId, cancellationToken);

        var change = suggestion?.Changes.FirstOrDefault(c => c.Id == changeId);
        if (suggestion is null || change is null)
        {
            return null;
        }

        return new LoadedChange(suggestion, change);
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

            // One summary email per resolution. Conflicted is admin-facing only
            // (it surfaces in the admin queue), so we don't email the user on it.
            if (notifications is not null &&
                newStatus is EditSuggestionStatus.Approved or EditSuggestionStatus.Rejected or EditSuggestionStatus.PartiallyApproved)
            {
                var recipient = recipients is null
                    ? default
                    : await recipients.ResolveAsync(suggestion.UserId, cancellationToken);
                await notifications.NotifySuggestionResolvedAsync(
                    suggestion, recipient.Email, cancellationToken);
            }
        }
    }

    private sealed record ChangeApplyContext(
        string ApprovingUserId,
        int SuggestionId,
        int ChangeId,
        string? OriginalSnapshotJson) : IChangeApplyContext;

    private sealed record LoadedChange(EditSuggestion Suggestion, EditSuggestionChange Change);
}
