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
    }

    protected override string MissingTargetMessage()
        => $"Disc '{this.Proposed.TargetEntityKey}' no longer exists.";

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

        var drifted = new StringBuilder();
        AppendIfDifferent(drifted, nameof(original.Name), original.Name, current.Name);
        AppendIfDifferent(drifted, nameof(original.Format), original.Format, current.Format);

        return drifted.Length == 0
            ? null
            : "Disc has been modified since the suggestion was submitted: " + drifted.ToString().TrimEnd(',', ' ');
    }

    public static DiscFieldsDetails SnapshotFrom(Disc disc, string? mediaItemSlug, string? boxsetSlug, string releaseSlug)
    {
        ArgumentNullException.ThrowIfNull(disc);
        return new DiscFieldsDetails(
            MediaItemSlug: mediaItemSlug,
            BoxsetSlug: boxsetSlug,
            ReleaseSlug: releaseSlug,
            DiscSlug: disc.Slug,
            DiscIndex: disc.Index,
            Name: disc.Name,
            Format: disc.Format);
    }

    /// <summary>
    /// Resolves the target <see cref="Disc"/> by navigating
    /// parent-slug → release-slug → disc-slug-or-index. Prefers slug match
    /// when a non-empty proposed slug is supplied; falls back to index only
    /// when proposed slug is null/empty. Returns null on any miss; does NOT
    /// use <c>AsNoTracking</c> so the apply path mutates the same tracked row.
    /// </summary>
    internal static async Task<Disc?> ResolveDiscAsync(
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
                .FirstOrDefaultAsync(
                    r => r.Slug == releaseSlug && r.MediaItem != null && r.MediaItem.Slug == ms,
                    cancellationToken);
        }
        else
        {
            var bs = details.BoxsetSlug;
            release = await context.Releases
                .Include(r => r.Discs)
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
