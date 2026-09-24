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

    /// <summary>The Disc ID belongs to a different release-disc, or this disc already has a different id; a pending change was filed for review, DB untouched.</summary>
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

public sealed record AttachFingerprintResult(
    AttachDiscIdOutcome Outcome,
    string ContentHash,
    string? MediaItemSlug,
    string? BoxsetSlug,
    string? MediaItemType,
    string? ReleaseSlug,
    string? DiscSlug,
    int? DiscIndex,
    string Fingerprint,
    string? ExistingFingerprint,
    bool MatchedDifferentDisc = false);

public sealed record AttachDiscIdentifiersResult(
    AttachDiscIdResult? GlobalDiscId,
    AttachFingerprintResult Fingerprint);

/// <summary>Optional target disc identity (supplied by the disc-detail CTA flow).</summary>
public sealed record DiscTargetIdentity(
    string? MediaItemSlug,
    string? BoxsetSlug,
    string? ReleaseSlug,
    string? DiscSlug,
    int? DiscIndex);

/// <summary>
/// A media-item release-disc matching a submitted content hash. The target uses only stable
/// natural keys; the remaining fields provide user-friendly labels for release selection.
/// </summary>
public sealed record DiscIdTargetCandidate(
    DiscTargetIdentity Target,
    string? MediaTitle,
    string MediaItemSlug,
    string? ReleaseTitle,
    string ReleaseSlug,
    string? DiscName,
    string? DiscSlug,
    int DiscIndex,
    string? CurrentGlobalDiscId);

public interface IDiscIdBackfillService
{
    Task<IReadOnlyList<DiscIdTargetCandidate>> GetTargetsByContentHashAsync(
        string contentHash,
        CancellationToken cancellationToken = default);

    Task<AttachDiscIdResult> AttachAsync(
        string userId,
        string contentHash,
        string globalDiscId,
        DiscTargetIdentity? target,
        CancellationToken cancellationToken = default);

    Task<AttachFingerprintResult> AttachFingerprintAsync(
        string userId,
        string contentHash,
        string fingerprint,
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

    public async Task<IReadOnlyList<DiscIdTargetCandidate>> GetTargetsByContentHashAsync(
        string contentHash,
        CancellationToken cancellationToken = default)
        => await database.Set<ReleaseDisc>()
            .AsNoTracking()
            .Where(rd => rd.Disc != null && rd.Disc.ContentHash == contentHash)
            .Where(rd => rd.Release != null && rd.Release.MediaItem != null && rd.Release.Boxset == null)
            .Where(rd => rd.Release!.MediaItem!.Slug != null && rd.Release.Slug != null)
            .OrderBy(rd => rd.Release!.MediaItem!.Title)
            .ThenBy(rd => rd.Release!.MediaItem!.Slug)
            .ThenBy(rd => rd.Release!.Title)
            .ThenBy(rd => rd.Release!.Year)
            .ThenBy(rd => rd.Release!.Slug)
            .ThenBy(rd => rd.Index)
            .ThenBy(rd => rd.Slug)
            .Select(rd => new DiscIdTargetCandidate(
                new DiscTargetIdentity(
                    rd.Release!.MediaItem!.Slug!,
                    null,
                    rd.Release.Slug!,
                    rd.Slug,
                    rd.Index),
                rd.Release.MediaItem.Title,
                rd.Release.MediaItem.Slug!,
                rd.Release.Title,
                rd.Release.Slug!,
                rd.Name,
                rd.Slug,
                rd.Index,
                rd.GlobalDiscId))
            .ToListAsync(cancellationToken);

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

            // Disc-detail CTA: the user is on a specific disc's page. If the target disc already has
            // a *known* content-hash and it doesn't match the inserted disc, they inserted a
            // different disc. Rather than rejecting it, see whether the inserted disc matches some
            // other disc in the database that could use this Disc ID, and update that one instead.
            // If the target disc has no content-hash yet (the common "backfill" case), there's
            // nothing to compare against, so treat it as the intended target.
            if (targetDisc?.Disc is not null
                && !string.IsNullOrEmpty(targetDisc.Disc.ContentHash)
                && !string.Equals(targetDisc.Disc.ContentHash, contentHash, StringComparison.OrdinalIgnoreCase))
            {
                var altDisc = await ResolveByContentHashAsync(contentHash, cancellationToken);
                if (altDisc?.Disc is null)
                {
                    // Not the target, and not otherwise known -> genuine mismatch.
                    return BuildResult(AttachDiscIdOutcome.Mismatch, contentHash, targetDisc, globalDiscId, targetDisc.GlobalDiscId);
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

    public async Task<AttachFingerprintResult> AttachFingerprintAsync(
        string userId,
        string contentHash,
        string fingerprint,
        DiscTargetIdentity? target,
        CancellationToken cancellationToken = default)
    {
        var hasTarget = target is not null
            && !string.IsNullOrWhiteSpace(target.ReleaseSlug)
            && (!string.IsNullOrWhiteSpace(target.MediaItemSlug)
                || !string.IsNullOrWhiteSpace(target.BoxsetSlug));

        ReleaseDisc? releaseDisc;
        var matchedDifferentDisc = false;
        if (hasTarget)
        {
            var targetDisc = await ResolveByIdentityAsync(target!, cancellationToken);
            // As in AttachAsync: only treat this as a possible mismatch when the target disc already
            // has a known content-hash that differs. A disc with no content-hash yet (the common
            // "backfill" case) has nothing to compare against, so treat it as the intended target.
            if (targetDisc?.Disc is not null
                && !string.IsNullOrEmpty(targetDisc.Disc.ContentHash)
                && !string.Equals(
                    targetDisc.Disc.ContentHash,
                    contentHash,
                    StringComparison.OrdinalIgnoreCase))
            {
                releaseDisc = await ResolveByContentHashAsync(contentHash, cancellationToken);
                if (releaseDisc?.Disc is null)
                {
                    return BuildMatrixResult(
                        AttachDiscIdOutcome.Mismatch,
                        contentHash,
                        targetDisc,
                        fingerprint,
                        targetDisc.EffectiveFingerprint());
                }

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
            return new AttachFingerprintResult(
                AttachDiscIdOutcome.NotFound,
                contentHash,
                null,
                null,
                null,
                null,
                null,
                null,
                fingerprint,
                null);
        }

        return await ApplyFingerprintAsync(
            userId,
            contentHash,
            fingerprint,
            releaseDisc,
            matchedDifferentDisc,
            cancellationToken);
    }

    private async Task<AttachFingerprintResult> ApplyFingerprintAsync(
        string userId,
        string contentHash,
        string fingerprint,
        ReleaseDisc releaseDisc,
        bool matchedDifferentDisc,
        CancellationToken cancellationToken)
    {
        AttachFingerprintResult Result(
            AttachDiscIdOutcome outcome,
            string? existingFingerprint)
            => BuildMatrixResult(
                outcome,
                contentHash,
                releaseDisc,
                fingerprint,
                existingFingerprint,
                matchedDifferentDisc);

        var snapshot = DiscFieldsUpdate.SnapshotFrom(
            releaseDisc,
            releaseDisc.Release?.MediaItem?.Slug,
            releaseDisc.Release?.Boxset?.Slug,
            releaseDisc.Release?.Slug ?? string.Empty);
        var proposed = snapshot with { Fingerprint = fingerprint };
        var proposedJson = JsonSerializer.Serialize(proposed, JsonOptions);
        var snapshotJson = JsonSerializer.Serialize(snapshot, JsonOptions);

        var owner = await database.Set<ReleaseDisc>()
            .Where(rd => rd.Fingerprint == fingerprint)
            .Select(rd => new { rd.DiscId })
            .FirstOrDefaultAsync(cancellationToken);
        if (owner is not null)
        {
            if (owner.DiscId == releaseDisc.DiscId)
            {
                return Result(AttachDiscIdOutcome.AlreadyRecorded, fingerprint);
            }

            await FileConflictAsync(
                userId,
                $"Fingerprint conflict: {fingerprint} is already assigned to a different canonical disc.",
                proposedJson,
                snapshotJson,
                cancellationToken);
            return Result(AttachDiscIdOutcome.Conflict, fingerprint);
        }

        var siblingValues = await database.Set<ReleaseDisc>()
            .Where(rd => rd.DiscId == releaseDisc.DiscId)
            .Select(rd => rd.Fingerprint)
            .ToListAsync(cancellationToken);
        var effective = ReleaseDiscExtensions.EffectiveFingerprint(siblingValues);
        if (!string.IsNullOrEmpty(effective))
        {
            await FileConflictAsync(
                userId,
                $"Fingerprint conflict: this disc already has {effective}, but {fingerprint} was submitted.",
                proposedJson,
                snapshotJson,
                cancellationToken);
            return Result(AttachDiscIdOutcome.Conflict, effective);
        }

        var suggestion = await editSuggestionService.SubmitAsync(
            userId,
            EditSuggestionSource.GraphQL,
            $"Backfill fingerprint {fingerprint}",
            new List<SubmitChangeInput>
            {
                new(DiscFieldsUpdate.Key, proposedJson, snapshotJson),
            },
            cancellationToken);
        var appliedChange = await reviewService.ApproveChangeAsync(
            suggestion.Id,
            suggestion.Changes.First().Id,
            userId,
            adminNote: "Auto-approved add-only fingerprint backfill",
            cancellationToken);
        var persisted = await database.Set<ReleaseDisc>()
            .Where(rd => rd.DiscId == releaseDisc.DiscId)
            .Select(rd => rd.Fingerprint)
            .ToListAsync(cancellationToken);

        if (appliedChange?.Status != EditSuggestionChangeStatus.Applied
            || !string.Equals(
                ReleaseDiscExtensions.EffectiveFingerprint(persisted),
                fingerprint,
                StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                "Fingerprint backfill was recorded but did not persist to the database.");
        }

        return Result(AttachDiscIdOutcome.Applied, null);
    }

    // Applies the Disc ID to a resolved release-disc: idempotent no-op when it already carries the
    // id; a pending (un-applied) conflict change when the id belongs to a *different* release-disc
    // (cross-pressing) or this disc already carries a *different* id (same-release re-press);
    // otherwise an auto-approved change + immediate write.
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

        var proposed = snapshot with { GlobalDiscId = globalDiscId };
        var proposedJson = JsonSerializer.Serialize(proposed, JsonOptions);
        var snapshotJson = JsonSerializer.Serialize(snapshot, JsonOptions);

        // Who (if anyone) already stores this exact id? The id is unique across release-discs.
        var owner = await database.Set<ReleaseDisc>()
            .Where(rd => rd.GlobalDiscId == globalDiscId)
            .Select(rd => new { rd.Id, rd.DiscId })
            .FirstOrDefaultAsync(cancellationToken);

        if (owner is not null)
        {
            // Stored on this release-disc, or on a sibling sharing the same canonical disc (same
            // content = same pressing): already recorded — the read-time fallback surfaces it, and
            // storing it again would violate the unique index. Nothing to do.
            if (owner.DiscId == releaseDisc.DiscId)
            {
                return Result(AttachDiscIdOutcome.AlreadyRecorded, globalDiscId);
            }

            // Stored on a release-disc of a *different* canonical disc → the same id maps to two
            // different contents; needs review.
            var crossNote = $"Disc ID conflict: submitted {globalDiscId} is already assigned to a different disc (different content). Needs review.";
            await FileConflictAsync(userId, crossNote, proposedJson, snapshotJson, cancellationToken);
            return Result(AttachDiscIdOutcome.Conflict, globalDiscId);
        }

        // The id isn't stored anywhere yet. Determine this pressing's effective id (its own, or the
        // single id shared by siblings of the same canonical disc).
        var siblingIds = await database.Set<ReleaseDisc>()
            .Where(rd => rd.DiscId == releaseDisc.DiscId)
            .Select(rd => rd.GlobalDiscId)
            .ToListAsync(cancellationToken);
        var effective = ReleaseDiscExtensions.EffectiveGlobalDiscId(siblingIds);

        // Divergence: this pressing already resolves to a different id (its own or a shared sibling
        // id). A different submission is a possible re-press / mis-scan → review, DB untouched.
        if (!string.IsNullOrEmpty(effective))
        {
            var note = $"Disc ID conflict: this disc already has {effective} but {globalDiscId} was submitted. Possible re-press/variant — needs review.";
            await FileConflictAsync(userId, note, proposedJson, snapshotJson, cancellationToken);
            return Result(AttachDiscIdOutcome.Conflict, effective);
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

        // Confirm the id persisted (the natural-key resolution during apply must have hit this
        // release-disc) before reporting success.
        var persisted = await database.Set<ReleaseDisc>()
            .Where(rd => rd.Id == releaseDisc.Id)
            .Select(rd => rd.GlobalDiscId)
            .FirstOrDefaultAsync(cancellationToken);

        if (appliedChange?.Status != EditSuggestionChangeStatus.Applied
            || !string.Equals(persisted, globalDiscId, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                "Disc ID backfill was recorded but did not persist to the database. The change is available for review.");
        }

        return Result(AttachDiscIdOutcome.Applied, null);
    }

    // Files a pending (un-applied) change carrying the conflict note for admin review, leaving the
    // database untouched.
    private async Task FileConflictAsync(
        string userId, string note, string proposedJson, string snapshotJson, CancellationToken cancellationToken)
    {
        var conflictSuggestion = await editSuggestionService.SubmitAsync(
            userId,
            EditSuggestionSource.GraphQL,
            note,
            new List<SubmitChangeInput> { new(DiscFieldsUpdate.Key, proposedJson, snapshotJson) },
            cancellationToken);

        var conflictChange = conflictSuggestion.Changes.First();
        conflictChange.ConflictReason = note;
        await database.SaveChangesAsync(cancellationToken);
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

    private static AttachFingerprintResult BuildMatrixResult(
        AttachDiscIdOutcome outcome,
        string contentHash,
        ReleaseDisc releaseDisc,
        string fingerprint,
        string? existingFingerprint,
        bool matchedDifferentDisc = false)
    {
        var snapshot = DiscFieldsUpdate.SnapshotFrom(
            releaseDisc,
            releaseDisc.Release?.MediaItem?.Slug,
            releaseDisc.Release?.Boxset?.Slug,
            releaseDisc.Release?.Slug ?? string.Empty);
        return new AttachFingerprintResult(
            outcome,
            contentHash,
            snapshot.MediaItemSlug,
            snapshot.BoxsetSlug,
            releaseDisc.Release?.MediaItem?.Type,
            snapshot.ReleaseSlug,
            snapshot.DiscSlug,
            snapshot.DiscIndex,
            fingerprint,
            existingFingerprint,
            matchedDifferentDisc);
    }

    private IQueryable<ReleaseDisc> BaseQuery()
        => database.Set<ReleaseDisc>()
            .Include(rd => rd.Disc!)
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
