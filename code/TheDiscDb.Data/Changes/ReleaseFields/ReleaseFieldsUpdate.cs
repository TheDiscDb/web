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

    protected override async Task<ReleaseFieldsDetails?> LoadCurrentSnapshotAsync(
        SqlServerDataContext context,
        CancellationToken cancellationToken)
    {
        // No AsNoTracking: when ApplyCoreAsync re-queries the same context, the
        // tracker satisfies the lookup, guaranteeing Apply operates on the same
        // row instance Validate just inspected.
        var release = await context.Releases
            .FirstOrDefaultAsync(r => r.Id == this.Proposed.ReleaseId, cancellationToken);
        return release is null ? null : SnapshotFrom(release);
    }

    protected override async Task ApplyCoreAsync(
        SqlServerDataContext context,
        IChangeApplyContext apply,
        ReleaseFieldsDetails? original,
        CancellationToken cancellationToken)
    {
        var release = await context.Releases
            .FirstOrDefaultAsync(r => r.Id == this.Proposed.ReleaseId, cancellationToken)
            ?? throw new InvalidOperationException(
                $"Release {this.Proposed.ReleaseId} not found at apply time. Validate should have caught this.");

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
        // Slug and image URLs are intentionally not touched here.
    }

    protected override string MissingTargetMessage()
        => $"Release {this.Proposed.ReleaseId} no longer exists.";

    /// <summary>
    /// Builds a <see cref="ReleaseFieldsDetails"/> snapshot from a loaded
    /// <see cref="Release"/>. Public so callers (e.g. the submit pipeline)
    /// can use the same shape when capturing the original at edit time.
    /// </summary>
    public static ReleaseFieldsDetails SnapshotFrom(Release release)
    {
        ArgumentNullException.ThrowIfNull(release);
        return new ReleaseFieldsDetails(
            ReleaseId: release.Id,
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
        if (original.ReleaseId != current.ReleaseId)
        {
            return $"ReleaseId changed ({original.ReleaseId} → {current.ReleaseId}).";
        }

        var drifted = new StringBuilder();
        AppendIfDifferent(drifted, "Title", original.Title, current.Title);
        AppendIfDifferent(drifted, "RegionCode", original.RegionCode, current.RegionCode);
        AppendIfDifferent(drifted, "Locale", original.Locale, current.Locale);
        AppendIfDifferent(drifted, "Year", original.Year, current.Year);
        AppendIfDifferent(drifted, "Upc", original.Upc, current.Upc);
        AppendIfDifferent(drifted, "Isbn", original.Isbn, current.Isbn);
        AppendIfDifferent(drifted, "Asin", original.Asin, current.Asin);
        AppendIfDifferent(drifted, "ReleaseDate", original.ReleaseDate, current.ReleaseDate);

        return drifted.Length == 0
            ? null
            : "Release has been modified since the suggestion was submitted: " + drifted.ToString().TrimEnd(',', ' ');
    }

    private static void AppendIfDifferent<T>(StringBuilder sb, string fieldName, T originalValue, T currentValue)
    {
        if (!Equals(originalValue, currentValue))
        {
            sb.Append(fieldName).Append(", ");
        }
    }
}

