namespace TheDiscDb.Data.Changes.DiscFields;

using System;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using TheDiscDb.InputModels;
using TheDiscDb.Web.Data;

/// <summary>
/// Updates the editable fields (Name, Format) of an existing <see cref="Disc"/>.
/// Identity is resolved via the natural-key composite
/// (parent slug, release slug, disc slug or index) — int ids are deliberately
/// avoided because the catalogue tables are designed to be truncated and rebuilt.
/// </summary>
public sealed class DiscFieldsUpdate : ChangeBase<DiscFieldsDetails>
{
    public const string Key = "disc.fields.update";

    public DiscFieldsUpdate(DiscFieldsDetails proposed)
        : base(proposed)
    {
    }

    public DiscFieldsUpdate(DiscFieldsDetails proposed, JsonSerializerOptions jsonOptions)
        : base(proposed, jsonOptions)
    {
    }

    public override string TypeKey => Key;

    public override string TargetEntityKey => this.Proposed.TargetEntityKey;

    protected override async Task<DiscFieldsDetails?> LoadCurrentSnapshotAsync(
        SqlServerDataContext context,
        CancellationToken cancellationToken)
    {
        var disc = await ResolveDiscAsync(context, this.Proposed, cancellationToken);
        return disc is null
            ? null
            : SnapshotFrom(disc, this.Proposed.MediaItemSlug, this.Proposed.BoxsetSlug, this.Proposed.ReleaseSlug);
    }

    protected override async Task ApplyCoreAsync(
        SqlServerDataContext context,
        IChangeApplyContext apply,
        DiscFieldsDetails? original,
        CancellationToken cancellationToken)
    {
        var disc = await ResolveDiscAsync(context, this.Proposed, cancellationToken)
            ?? throw new InvalidOperationException(
                $"Disc '{this.Proposed.TargetEntityKey}' not found at apply time. Validate should have caught this.");

        SetIfChanged(original, original?.Name, this.Proposed.Name, v => disc.Name = v);
        SetIfChanged(original, original?.Format, this.Proposed.Format, v => disc.Format = v);

        // ContentHash is add-only: it may be supplied for a disc that has none,
        // but an existing hash is immutable and must never be overwritten.
        if (string.IsNullOrEmpty(original?.ContentHash))
        {
            SetIfChanged(original, original?.ContentHash, this.Proposed.ContentHash, v => disc.ContentHash = v);
        }

        // GlobalDiscId is add-only, same rule as ContentHash.
        if (string.IsNullOrEmpty(original?.GlobalDiscId))
        {
            SetIfChanged(original, original?.GlobalDiscId, this.Proposed.GlobalDiscId, v => disc.GlobalDiscId = v);
        }

        SetIfChanged(original, original?.IsPartial ?? false, this.Proposed.IsPartial, v => disc.Disc!.IsPartial = v);
    }

    protected override string MissingTargetMessage()
        => $"Disc '{this.Proposed.TargetEntityKey}' no longer exists.";

    /// <summary>
    /// Guards the unique <c>(Format, ContentHash)</c> index: when this change introduces a new
    /// content hash, reject it up front if another disc already carries the same hash+format, rather
    /// than letting <c>SaveChanges</c> throw an opaque unique-constraint violation during apply.
    /// </summary>
    protected override async Task<ChangeValidationResult?> ValidateAdditionalAsync(
        SqlServerDataContext context,
        DiscFieldsDetails? original,
        DiscFieldsDetails current,
        CancellationToken cancellationToken)
    {
        // ContentHash is add-only; a collision is only possible when a new hash is being introduced
        // onto a disc that currently has none.
        var addingHash = !string.IsNullOrEmpty(this.Proposed.ContentHash)
            && string.IsNullOrEmpty(current.ContentHash);
        if (!addingHash)
        {
            return null;
        }

        var target = await ResolveDiscAsync(context, this.Proposed, cancellationToken);
        if (target?.Disc is null)
        {
            return null; // missing-target is handled by the standard validation path
        }

        var hash = this.Proposed.ContentHash;
        var format = string.IsNullOrEmpty(this.Proposed.Format) ? target.Disc.Format : this.Proposed.Format;
        var targetDiscId = target.Disc.Id;

        var conflictingDiscId = await context.Discs
            .Where(d => d.Id != targetDiscId && d.ContentHash == hash && d.Format == format)
            .Select(d => (int?)d.Id)
            .FirstOrDefaultAsync(cancellationToken);

        if (conflictingDiscId is not null)
        {
            return ChangeValidationResult.Conflict(
                $"Content hash {hash} is already assigned to a different {format} disc. Content hashes are " +
                "unique per format — the wrong disc may have been scanned or the hash entered for the wrong disc.");
        }

        return null;
    }

    protected override string? DescribeDrift(DiscFieldsDetails original, DiscFieldsDetails current)
    {
        if (original.ReleaseSlug != current.ReleaseSlug
            || original.MediaItemSlug != current.MediaItemSlug
            || original.BoxsetSlug != current.BoxsetSlug
            || original.DiscIndex != current.DiscIndex
            || original.DiscSlug != current.DiscSlug)
        {
            return $"Disc identity changed: snapshot '{original.TargetEntityKey}' vs current '{current.TargetEntityKey}'.";
        }

        // Only report drift on fields this suggestion is actually proposing to change.
        // Background data rebuilds may add Format or ContentHash to discs that had
        // neither; a name-only suggestion should not be blocked by that.
        var drifted = new StringBuilder();
        if (this.Proposed.Name != original.Name)
        {
            AppendIfDifferent(drifted, nameof(original.Name), original.Name, current.Name);
        }

        if (this.Proposed.Format != original.Format)
        {
            AppendIfDifferent(drifted, nameof(original.Format), original.Format, current.Format);
        }

        // ContentHash is add-only: only flag drift when a new hash is being proposed
        // (original was null/empty and proposed provides a value).
        if (!string.IsNullOrEmpty(this.Proposed.ContentHash) && string.IsNullOrEmpty(original.ContentHash))
        {
            AppendIfDifferent(drifted, nameof(original.ContentHash), original.ContentHash, current.ContentHash);
        }

        // GlobalDiscId is add-only: same drift rule as ContentHash.
        if (!string.IsNullOrEmpty(this.Proposed.GlobalDiscId) && string.IsNullOrEmpty(original.GlobalDiscId))
        {
            AppendIfDifferent(drifted, nameof(original.GlobalDiscId), original.GlobalDiscId, current.GlobalDiscId);
        }

        if (this.Proposed.IsPartial != original.IsPartial)
        {
            AppendIfDifferent(drifted, nameof(original.IsPartial), original.IsPartial, current.IsPartial);
        }

        return drifted.Length == 0
            ? null
            : "Disc has been modified since the suggestion was submitted: " + drifted.ToString().TrimEnd(',', ' ');
    }

    public static DiscFieldsDetails SnapshotFrom(IDisc disc, string? mediaItemSlug, string? boxsetSlug, string releaseSlug)
    {
        ArgumentNullException.ThrowIfNull(disc);

        // ContentHash isn't part of IDisc (the StrawberryShake-generated result
        // types also implement IDisc and don't surface it), so read it from the
        // concrete persistence models.
        var contentHash = disc switch
        {
            ReleaseDisc rd => rd.ContentHash,
            Disc d => d.ContentHash,
            _ => null,
        };

        var globalDiscId = disc switch
        {
            ReleaseDisc rd => rd.GlobalDiscId,
            Disc d => d.GlobalDiscId,
            _ => null,
        };

        var isPartial = disc switch
        {
            ReleaseDisc rd => rd.Disc?.IsPartial ?? false,
            Disc d => d.IsPartial,
            _ => false,
        };

        return new DiscFieldsDetails(
            MediaItemSlug: mediaItemSlug,
            BoxsetSlug: boxsetSlug,
            ReleaseSlug: releaseSlug,
            DiscSlug: disc.Slug,
            DiscIndex: disc.Index,
            Name: disc.Name,
            Format: disc.Format,
            ContentHash: contentHash,
            GlobalDiscId: globalDiscId,
            IsPartial: isPartial);
    }

    /// <summary>
    /// Resolves the target <see cref="Disc"/> by navigating
    /// parent-slug → release-slug → disc-slug-or-index. Prefers slug match
    /// when a non-empty proposed slug is supplied; falls back to index only
    /// when proposed slug is null/empty. Returns null on any miss; does NOT
    /// use <c>AsNoTracking</c> so the apply path mutates the same tracked row.
    /// </summary>
    internal static async Task<ReleaseDisc?> ResolveDiscAsync(
        SqlServerDataContext context,
        DiscFieldsDetails details,
        CancellationToken cancellationToken)
    {
        // Contract: exactly one parent slug must be populated. Both-set is just
        // as much a caller error as neither-set and must not silently resolve.
        var hasMedia = !string.IsNullOrWhiteSpace(details.MediaItemSlug);
        var hasBoxset = !string.IsNullOrWhiteSpace(details.BoxsetSlug);
        if (hasMedia == hasBoxset)
        {
            return null;
        }

        var releaseSlug = details.ReleaseSlug;
        Release? release;

        if (hasMedia)
        {
            var ms = details.MediaItemSlug;
            release = await context.Releases
                .Include(r => r.Discs)
                    .ThenInclude(d => d.Disc)
                .FirstOrDefaultAsync(
                    r => r.Slug == releaseSlug && r.MediaItem != null && r.MediaItem.Slug == ms,
                    cancellationToken);
        }
        else
        {
            var bs = details.BoxsetSlug;
            release = await context.Releases
                .Include(r => r.Discs)
                    .ThenInclude(d => d.Disc)
                .FirstOrDefaultAsync(
                    r => r.Slug == releaseSlug && r.Boxset != null && r.Boxset.Slug == bs,
                    cancellationToken);
        }

        if (release is null)
        {
            return null;
        }

        if (!string.IsNullOrEmpty(details.DiscSlug))
        {
            return release.Discs.FirstOrDefault(d => d.Slug == details.DiscSlug);
        }

        return release.Discs.FirstOrDefault(d => d.Index == details.DiscIndex);
    }
}
