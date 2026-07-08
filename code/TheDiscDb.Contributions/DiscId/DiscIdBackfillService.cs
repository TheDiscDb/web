namespace TheDiscDb.Services.DiscId;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using TheDiscDb.Data.Changes.DiscFields;
using TheDiscDb.InputModels;
using TheDiscDb.Services.EditSuggestions;
using TheDiscDb.Web.Data;

public enum AttachDiscIdOutcome
{
    /// <summary>Disc had no Disc ID; it was written and an approved (applied) change was recorded for /data sync.</summary>
    Applied,

    /// <summary>The disc already carried this exact Disc ID; nothing to do.</summary>
    AlreadyRecorded,

    /// <summary>The disc already has a different Disc ID; a pending change was filed for review, DB untouched.</summary>
    Conflict,

    /// <summary>No disc in the database matched the submitted content-hash / identity.</summary>
    NotFound,

    /// <summary>
    /// A target disc was specified (disc-detail CTA) and found, but the inserted disc's
    /// content-hash matches neither that disc nor any other disc in the database — the user
    /// likely inserted a disc we don't know about. Nothing is written.
    /// </summary>
    Mismatch,
}

/// <summary>Result of an <see cref="IDiscIdBackfillService.AttachAsync"/> call.</summary>
public sealed record AttachDiscIdResult(
    AttachDiscIdOutcome Outcome,
    string ContentHash,
    string? MediaItemSlug,
    string? BoxsetSlug,
    string? MediaItemType,
    string? ReleaseSlug,
    string? DiscSlug,
    int? DiscIndex,
    string? GlobalDiscId,
    string? ExistingGlobalDiscId,
    /// <summary>
    /// True when the CTA target disc didn't match the inserted disc, but the inserted disc matched a
    /// <em>different</em> disc in the database (identified by content-hash) that we updated instead.
    /// </summary>
    bool MatchedDifferentDisc = false);

/// <summary>Optional target disc identity (supplied by the disc-detail CTA flow).</summary>
public sealed record DiscTargetIdentity(
    string? MediaItemSlug,
    string? BoxsetSlug,
    string? ReleaseSlug,
    string? DiscSlug,
    int? DiscIndex);

public interface IDiscIdBackfillService
{
    Task<AttachDiscIdResult> AttachAsync(
        string userId,
        string contentHash,
        string globalDiscId,
        DiscTargetIdentity? target,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Backfills a globally-stable Disc ID onto an existing disc matched by content-hash.
/// Clean adds are written immediately and recorded as an approved (applied) change so the
/// <c>/data</c> batch import picks them up; a submission that conflicts with an existing,
/// different Disc ID leaves the database untouched and files a pending change (with a note)
/// for admin review.
/// </summary>
public sealed class DiscIdBackfillService(
    SqlServerDataContext database,
    IEditSuggestionService editSuggestionService,
    IEditSuggestionReviewService reviewService) : IDiscIdBackfillService
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task<AttachDiscIdResult> AttachAsync(
        string userId,
        string contentHash,
        string globalDiscId,
        DiscTargetIdentity? target,
        CancellationToken cancellationToken = default)
    {
        var hasTarget = target is not null
            && !string.IsNullOrWhiteSpace(target.ReleaseSlug)
            && (!string.IsNullOrWhiteSpace(target.MediaItemSlug) || !string.IsNullOrWhiteSpace(target.BoxsetSlug));

        ReleaseDisc? releaseDisc;
        var matchedDifferentDisc = false;

        if (hasTarget)
        {
            var targetDisc = await ResolveByIdentityAsync(target!, cancellationToken);

            // Disc-detail CTA: the user is on a specific disc's page. If the inserted disc's
            // content-hash doesn't match that disc, they inserted a different disc. Rather than
            // rejecting it, see whether the inserted disc matches some other disc in the database
            // that could use this Disc ID, and update that one instead.
            if (targetDisc?.Disc is not null
                && !string.Equals(targetDisc.Disc.ContentHash, contentHash, StringComparison.OrdinalIgnoreCase))
            {
                var altDisc = await ResolveByContentHashAsync(contentHash, cancellationToken);
                if (altDisc?.Disc is null)
                {
                    // Not the target, and not otherwise known -> genuine mismatch.
                    return BuildResult(AttachDiscIdOutcome.Mismatch, contentHash, targetDisc, globalDiscId, targetDisc.Disc.GlobalDiscId);
                }

                releaseDisc = altDisc;
                matchedDifferentDisc = true;
            }
            else
            {
                releaseDisc = targetDisc;
            }
        }
        else
        {
            releaseDisc = await ResolveByContentHashAsync(contentHash, cancellationToken);
        }

        if (releaseDisc?.Disc is null)
        {
            return new AttachDiscIdResult(AttachDiscIdOutcome.NotFound, contentHash, null, null, null, null, null, null, globalDiscId, null);
        }

        return await ApplyAsync(userId, contentHash, globalDiscId, releaseDisc, matchedDifferentDisc, cancellationToken);
    }

    // Applies the Disc ID to a resolved disc: idempotent no-op when already recorded, a pending
    // (un-applied) change on conflict, or an auto-approved change + immediate write on a clean add.
    private async Task<AttachDiscIdResult> ApplyAsync(
        string userId,
        string contentHash,
        string globalDiscId,
        ReleaseDisc releaseDisc,
        bool matchedDifferentDisc,
        CancellationToken cancellationToken)
    {
        var parentMediaSlug = releaseDisc.Release?.MediaItem?.Slug;
        var parentBoxsetSlug = releaseDisc.Release?.Boxset?.Slug;
        var parentMediaType = releaseDisc.Release?.MediaItem?.Type;
        var parentReleaseSlug = releaseDisc.Release?.Slug ?? string.Empty;

        var snapshot = DiscFieldsUpdate.SnapshotFrom(releaseDisc, parentMediaSlug, parentBoxsetSlug, parentReleaseSlug);

        AttachDiscIdResult Result(AttachDiscIdOutcome outcome, string? existingGlobalDiscId) => new(
            outcome, contentHash,
            parentMediaSlug, parentBoxsetSlug, parentMediaType, parentReleaseSlug, snapshot.DiscSlug, snapshot.DiscIndex,
            globalDiscId, existingGlobalDiscId, matchedDifferentDisc);

        var current = releaseDisc.Disc!.GlobalDiscId;

        // Already carries this exact Disc ID -> idempotent no-op.
        if (string.Equals(current, globalDiscId, StringComparison.OrdinalIgnoreCase))
        {
            return Result(AttachDiscIdOutcome.AlreadyRecorded, current);
        }

        // Only GlobalDiscId changes (all other fields equal the snapshot, so add-only applies just the id).
        var proposed = snapshot with { GlobalDiscId = globalDiscId };
        var proposedJson = JsonSerializer.Serialize(proposed, JsonOptions);
        var snapshotJson = JsonSerializer.Serialize(snapshot, JsonOptions);

        // Conflict: a different Disc ID already exists -> file a pending change with a note, do NOT touch the DB.
        if (!string.IsNullOrEmpty(current))
        {
            var note = $"Disc ID conflict: submitted {globalDiscId} but the disc already has {current}. Possible re-press/variant — needs review.";
            var conflictSuggestion = await editSuggestionService.SubmitAsync(
                userId,
                EditSuggestionSource.GraphQL,
                note,
                new List<SubmitChangeInput> { new(DiscFieldsUpdate.Key, proposedJson, snapshotJson) },
                cancellationToken);

            var conflictChange = conflictSuggestion.Changes.First();
            conflictChange.ConflictReason = note;
            await database.SaveChangesAsync(cancellationToken);

            return Result(AttachDiscIdOutcome.Conflict, current);
        }

        // Clean add: submit + auto-approve so the DB is written now and the change is Applied for /data sync.
        var suggestion = await editSuggestionService.SubmitAsync(
            userId,
            EditSuggestionSource.GraphQL,
            $"Backfill Disc ID {globalDiscId}",
            new List<SubmitChangeInput> { new(DiscFieldsUpdate.Key, proposedJson, snapshotJson) },
            cancellationToken);

        var changeId = suggestion.Changes.First().Id;
        var appliedChange = await reviewService.ApproveChangeAsync(
            suggestion.Id, changeId, userId, adminNote: "Auto-approved add-only Disc ID backfill", cancellationToken);

        // Confirm the Disc ID actually persisted before reporting success. ReleaseDisc.GlobalDiscId
        // is a passthrough to the canonical Disc; if that navigation wasn't loaded during apply the
        // write would silently no-op, so we verify against the database rather than trust the status.
        var persisted = await database.Set<ReleaseDisc>()
            .Where(rd => rd.Id == releaseDisc.Id)
            .Select(rd => rd.Disc!.GlobalDiscId)
            .FirstOrDefaultAsync(cancellationToken);

        if (appliedChange?.Status != EditSuggestionChangeStatus.Applied
            || !string.Equals(persisted, globalDiscId, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                "Disc ID backfill was recorded but did not persist to the database. The change is available for review.");
        }

        return Result(AttachDiscIdOutcome.Applied, null);
    }

    // Builds a terminal (non-applying) result carrying the given disc's identity for display.
    private static AttachDiscIdResult BuildResult(
        AttachDiscIdOutcome outcome,
        string contentHash,
        ReleaseDisc releaseDisc,
        string globalDiscId,
        string? existingGlobalDiscId)
    {
        var parentMediaSlug = releaseDisc.Release?.MediaItem?.Slug;
        var parentBoxsetSlug = releaseDisc.Release?.Boxset?.Slug;
        var parentMediaType = releaseDisc.Release?.MediaItem?.Type;
        var parentReleaseSlug = releaseDisc.Release?.Slug ?? string.Empty;
        var snapshot = DiscFieldsUpdate.SnapshotFrom(releaseDisc, parentMediaSlug, parentBoxsetSlug, parentReleaseSlug);

        return new AttachDiscIdResult(
            outcome, contentHash,
            parentMediaSlug, parentBoxsetSlug, parentMediaType, parentReleaseSlug, snapshot.DiscSlug, snapshot.DiscIndex,
            globalDiscId, existingGlobalDiscId);
    }

    private IQueryable<ReleaseDisc> BaseQuery()
        => database.Set<ReleaseDisc>()
            .Include(rd => rd.Disc)
            .Include(rd => rd.Release!).ThenInclude(r => r.MediaItem)
            .Include(rd => rd.Release!).ThenInclude(r => r.Boxset);

    // Disc-detail CTA: resolve the specific disc by its natural-key identity (no content-hash check;
    // the caller compares the hash so it can distinguish "wrong physical disc" from "not found").
    private async Task<ReleaseDisc?> ResolveByIdentityAsync(DiscTargetIdentity target, CancellationToken cancellationToken)
    {
        var releaseSlug = target.ReleaseSlug;
        var query = BaseQuery().Where(rd => rd.Release!.Slug == releaseSlug);

        query = !string.IsNullOrWhiteSpace(target.MediaItemSlug)
            ? query.Where(rd => rd.Release!.MediaItem != null && rd.Release.MediaItem.Slug == target.MediaItemSlug)
            : query.Where(rd => rd.Release!.Boxset != null && rd.Release.Boxset.Slug == target.BoxsetSlug);

        query = !string.IsNullOrEmpty(target.DiscSlug)
            ? query.Where(rd => rd.Slug == target.DiscSlug)
            : query.Where(rd => rd.Index == target.DiscIndex);

        return await query.FirstOrDefaultAsync(cancellationToken);
    }

    // Dedicated page: resolve by content-hash alone.
    private async Task<ReleaseDisc?> ResolveByContentHashAsync(string contentHash, CancellationToken cancellationToken)
        => await BaseQuery().FirstOrDefaultAsync(rd => rd.Disc != null && rd.Disc.ContentHash == contentHash, cancellationToken);
}
