namespace TheDiscDb.Data.Changes.ReleaseFields;

using System;

/// <summary>
/// Payload for the <c>release.fields.update</c> change type. Carries the editable
/// (non-slug, non-image) fields of a <see cref="TheDiscDb.InputModels.Release"/>.
/// Used both as the proposed payload (what the user wants the Release to look like)
/// and as the original-snapshot payload (what the user saw at edit time).
/// </summary>
/// <remarks>
/// Slug is intentionally absent: slugs appear in public URLs and are not editable.
/// Image URLs are managed by <c>release.image.update</c>, not this change type.
/// </remarks>
public sealed record ReleaseFieldsDetails(
    int ReleaseId,
    string? Title,
    string? RegionCode,
    string? Locale,
    int Year,
    string? Upc,
    string? Isbn,
    string? Asin,
    DateTimeOffset ReleaseDate);
