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
/// Slug and Index are NOT editable here: slug appears in public URLs and index is
/// positional identity. <see cref="ContentHash"/> is normally computed during
/// import, but is editable here as an <b>add-only</b> field: users may supply a
/// hash for a disc that has none, while an existing hash is immutable and must
/// never be overwritten by a suggestion (enforced at apply time).
/// <see cref="GlobalDiscId"/> follows the same add-only rule.
/// </remarks>
public sealed record DiscFieldsDetails(
    string? MediaItemSlug,
    string? BoxsetSlug,
    string ReleaseSlug,
    string? DiscSlug,
    int DiscIndex,
    string? Name,
    string? Format,
    string? ContentHash = null,
    string? GlobalDiscId = null,
    bool IsPartial = false)
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
