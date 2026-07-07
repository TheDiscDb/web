namespace TheDiscDb.Data.Changes.ReleaseFields;

using System;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using TheDiscDb.InputModels;
using TheDiscDb.Web.Data;

/// <summary>
/// Updates the non-slug, non-image fields of an existing <see cref="Release"/>.
/// Snapshot-drift detection compares the user-supplied original snapshot against
/// the current database row; any difference on a tracked field is a conflict.
/// All drift orchestration (snapshot deserialisation, TOCTOU revalidation in
/// <see cref="ChangeBase{TDetails}.ApplyAsync"/>) is inherited from
/// <see cref="ChangeBase{TDetails}"/>.
/// </summary>
public sealed class ReleaseFieldsUpdate : ChangeBase<ReleaseFieldsDetails>
{
    public const string Key = "release.fields.update";

    public ReleaseFieldsUpdate(ReleaseFieldsDetails proposed)
        : base(proposed)
    {
    }

    public ReleaseFieldsUpdate(ReleaseFieldsDetails proposed, JsonSerializerOptions jsonOptions)
        : base(proposed, jsonOptions)
    {
    }

    public override string TypeKey => Key;

    public override string TargetEntityKey => this.Proposed.TargetEntityKey;

    protected override async Task<ReleaseFieldsDetails?> LoadCurrentSnapshotAsync(
        SqlServerDataContext context,
        CancellationToken cancellationToken)
    {
        // Identity is the natural-key slug composite. Exactly one of
        // MediaItemSlug/BoxsetSlug is populated; we match the parent navigation
        // accordingly. No AsNoTracking — keeping the entity in the change tracker
        // means ApplyCoreAsync's subsequent lookup is a tracker hit, guaranteeing
        // Apply operates on the same row instance Validate just inspected.
        var releaseSlug = this.Proposed.ReleaseSlug;
        Release? release;

        if (!string.IsNullOrWhiteSpace(this.Proposed.MediaItemSlug))
        {
            var parentSlug = this.Proposed.MediaItemSlug;
            release = await context.Releases
                .FirstOrDefaultAsync(
                    r => r.Slug == releaseSlug
                        && r.MediaItem != null
                        && r.MediaItem.Slug == parentSlug,
                    cancellationToken);
        }
        else if (!string.IsNullOrWhiteSpace(this.Proposed.BoxsetSlug))
        {
            var parentSlug = this.Proposed.BoxsetSlug;
            release = await context.Releases
                .FirstOrDefaultAsync(
                    r => r.Slug == releaseSlug
                        && r.Boxset != null
                        && r.Boxset.Slug == parentSlug,
                    cancellationToken);
        }
        else
        {
            // Neither parent slug supplied — caller error. Validate returns Conflict
            // (via MissingTargetMessage) rather than a hard exception so the admin
            // queue surfaces it consistently with other resolution failures.
            return null;
        }

        return release is null ? null : SnapshotFrom(release, this.Proposed.MediaItemSlug, this.Proposed.BoxsetSlug);
    }

    protected override async Task ApplyCoreAsync(
        SqlServerDataContext context,
        IChangeApplyContext apply,
        ReleaseFieldsDetails? original,
        CancellationToken cancellationToken)
    {
        var releaseSlug = this.Proposed.ReleaseSlug;
        Release? release;

        if (!string.IsNullOrWhiteSpace(this.Proposed.MediaItemSlug))
        {
            var parentSlug = this.Proposed.MediaItemSlug;
            release = await context.Releases
                .FirstOrDefaultAsync(
                    r => r.Slug == releaseSlug
                        && r.MediaItem != null
                        && r.MediaItem.Slug == parentSlug,
                    cancellationToken);
        }
        else if (!string.IsNullOrWhiteSpace(this.Proposed.BoxsetSlug))
        {
            var parentSlug = this.Proposed.BoxsetSlug;
            release = await context.Releases
                .FirstOrDefaultAsync(
                    r => r.Slug == releaseSlug
                        && r.Boxset != null
                        && r.Boxset.Slug == parentSlug,
                    cancellationToken);
        }
        else
        {
            release = null;
        }

        if (release is null)
        {
            throw new InvalidOperationException(
                $"Release '{this.Proposed.TargetEntityKey}' not found at apply time. Validate should have caught this.");
        }

        // Snapshot-diff semantics: only write the fields the user actually changed
        // (proposed != snapshot). A null Proposed.X is meaningful only when the
        // snapshot.X was non-null (an explicit clear); otherwise it means
        // "field left alone in the form" and we must NOT overwrite the current row.
        SetIfChanged(original, original?.Title, this.Proposed.Title, v => release.Title = v);
        SetIfChanged(original, original?.RegionCode, this.Proposed.RegionCode, v => release.RegionCode = v);
        SetIfChanged(original, original?.Locale, this.Proposed.Locale, v => release.Locale = v);
        SetIfChanged(original, original?.Year ?? 0, this.Proposed.Year, v => release.Year = v);
        SetIfChanged(original, original?.Upc, this.Proposed.Upc, v => release.Upc = v);
        SetIfChanged(original, original?.Isbn, this.Proposed.Isbn, v => release.Isbn = v);
        SetIfChanged(original, original?.Asin, this.Proposed.Asin, v => release.Asin = v);
        SetIfChanged(original, original?.ReleaseDate ?? default, this.Proposed.ReleaseDate, v => release.ReleaseDate = v);
        // Slug, parent-slug navigation, and image URLs are intentionally not touched here.
    }

    protected override string MissingTargetMessage()
        => $"Release '{this.Proposed.TargetEntityKey}' no longer exists.";

    /// <summary>
    /// Builds a <see cref="ReleaseFieldsDetails"/> snapshot from a loaded
    /// <see cref="Release"/>. Public so callers (e.g. the submit pipeline)
    /// can use the same shape when capturing the original at edit time. The
    /// parent slug pair is supplied by the caller — the release entity itself
    /// only carries an int FK, and we don't want to trigger lazy-loads here.
    /// </summary>
    public static ReleaseFieldsDetails SnapshotFrom(Release release, string? mediaItemSlug, string? boxsetSlug)
    {
        ArgumentNullException.ThrowIfNull(release);
        return new ReleaseFieldsDetails(
            MediaItemSlug: mediaItemSlug,
            BoxsetSlug: boxsetSlug,
            ReleaseSlug: release.Slug ?? string.Empty,
            Title: release.Title,
            RegionCode: release.RegionCode,
            Locale: release.Locale,
            Year: release.Year,
            Upc: release.Upc,
            Isbn: release.Isbn,
            Asin: release.Asin,
            ReleaseDate: release.ReleaseDate);
    }

    protected override string? DescribeDrift(ReleaseFieldsDetails original, ReleaseFieldsDetails current)
    {
        // The natural-key slugs are stable by design (non-editable, appear in
        // public URLs). If they ever differ between snapshot and current that
        // indicates either a data corruption or a code bug — flag explicitly.
        if (original.ReleaseSlug != current.ReleaseSlug
            || original.MediaItemSlug != current.MediaItemSlug
            || original.BoxsetSlug != current.BoxsetSlug)
        {
            return $"Release identity changed: snapshot '{original.TargetEntityKey}' vs current '{current.TargetEntityKey}'.";
        }

        // Only report drift on fields this suggestion is actually proposing to change.
        // A rebuild may fill in previously-null fields (UPC, ASIN, etc.); a title-only
        // suggestion must not be blocked by drift in fields it never touched.
        var drifted = new StringBuilder();
        if (this.Proposed.Title != original.Title)
            AppendIfDifferent(drifted, nameof(original.Title), original.Title, current.Title);
        if (this.Proposed.RegionCode != original.RegionCode)
            AppendIfDifferent(drifted, nameof(original.RegionCode), original.RegionCode, current.RegionCode);
        if (this.Proposed.Locale != original.Locale)
            AppendIfDifferent(drifted, nameof(original.Locale), original.Locale, current.Locale);
        if (this.Proposed.Year != original.Year)
            AppendIfDifferent(drifted, nameof(original.Year), original.Year, current.Year);
        if (this.Proposed.Upc != original.Upc)
            AppendIfDifferent(drifted, nameof(original.Upc), original.Upc, current.Upc);
        if (this.Proposed.Isbn != original.Isbn)
            AppendIfDifferent(drifted, nameof(original.Isbn), original.Isbn, current.Isbn);
        if (this.Proposed.Asin != original.Asin)
            AppendIfDifferent(drifted, nameof(original.Asin), original.Asin, current.Asin);
        if (this.Proposed.ReleaseDate != original.ReleaseDate)
            AppendIfDifferent(drifted, nameof(original.ReleaseDate), original.ReleaseDate, current.ReleaseDate);

        return drifted.Length == 0
            ? null
            : "Release has been modified since the suggestion was submitted: " + drifted.ToString().TrimEnd(',', ' ');
    }
}

