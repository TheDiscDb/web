namespace TheDiscDb.Data.Changes.DiscFields;

/// <summary>
/// Payload for the <c>disc.fields.update</c> change type. Carries the editable
/// (non-identity) fields of a <see cref="TheDiscDb.InputModels.Disc"/>, plus the
/// natural-key composite that identifies which disc to apply to.
/// </summary>
/// <remarks>
/// Identity is by slug pair (release parent) + disc-slug-or-index, never int id.
/// Exactly one of <see cref="MediaItemSlug"/> / <see cref="BoxsetSlug"/> must be
/// non-null. Disc.Slug is optional in the source data — when absent we fall back
/// to Disc.Index per the existing <c>SlugOrIndex()</c> extension convention.
/// Both <see cref="DiscSlug"/> and <see cref="DiscIndex"/> are carried so the
/// snapshot can detect drift on either; resolution prefers slug when non-empty.
/// Slug, Index, and ContentHash are NOT editable here: slug appears in public
/// URLs, index is positional identity, ContentHash is computed during import.
/// </remarks>
public sealed record DiscFieldsDetails(
    string? MediaItemSlug,
    string? BoxsetSlug,
    string ReleaseSlug,
    string? DiscSlug,
    int DiscIndex,
    string? Name,
    string? Format)
{
    /// <summary>
    /// Natural-key identifier suitable for <see cref="TheDiscDb.Web.Data.EditSuggestion.TargetEntityKey"/>:
    /// <c>"&lt;parentSlug&gt;/&lt;releaseSlug&gt;/&lt;discSlugOrIndex&gt;"</c>.
    /// </summary>
    public string TargetEntityKey =>
        $"{(this.MediaItemSlug ?? this.BoxsetSlug) ?? string.Empty}/{this.ReleaseSlug}/{DiscToken(this.DiscSlug, this.DiscIndex)}";

    internal static string DiscToken(string? discSlug, int discIndex)
        => string.IsNullOrEmpty(discSlug) ? "i" + discIndex.ToString(System.Globalization.CultureInfo.InvariantCulture) : discSlug;
}
