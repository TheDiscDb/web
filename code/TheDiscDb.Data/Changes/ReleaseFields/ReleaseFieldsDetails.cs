namespace TheDiscDb.Data.Changes.ReleaseFields;

using System;

/// <summary>
/// Payload for the <c>release.fields.update</c> change type. Carries the editable
/// (non-slug, non-image) fields of a <see cref="TheDiscDb.InputModels.Release"/>,
/// plus the natural-key slug composite that identifies which release to apply to.
/// Used both as the proposed payload (what the user wants the Release to look like)
/// and as the original-snapshot payload (what the user saw at edit time).
/// </summary>
/// <remarks>
/// Identity is by slug pair, not int id: the non-user data tables are designed to
/// be truncated and rebuilt from the file repo, which shifts int ids but preserves
/// slugs. Exactly one of <see cref="MediaItemSlug"/> / <see cref="BoxsetSlug"/>
/// must be non-null (a release belongs to one or the other) — this mirrors the
/// existing <c>ReleaseAffiliateLinks</c> CHECK-constraint convention.
/// Slug is intentionally absent from the editable fields: slugs appear in public
/// URLs and are not editable.
/// Image URLs are managed by <c>release.image.update</c>, not this change type.
/// </remarks>
public sealed record ReleaseFieldsDetails(
    string? MediaItemSlug,
    string? BoxsetSlug,
    string ReleaseSlug,
    string? Title,
    string? RegionCode,
    string? Locale,
    int Year,
    string? Upc,
    string? Isbn,
    string? Asin,
    DateTimeOffset ReleaseDate)
{
    /// <summary>
    /// The natural-key identifier suitable for
    /// <see cref="TheDiscDb.Web.Data.EditSuggestion.TargetEntityKey"/>:
    /// <c>"&lt;parentSlug&gt;/&lt;releaseSlug&gt;"</c>.
    /// </summary>
    public string TargetEntityKey =>
        $"{(this.MediaItemSlug ?? this.BoxsetSlug) ?? string.Empty}/{this.ReleaseSlug}";
}

