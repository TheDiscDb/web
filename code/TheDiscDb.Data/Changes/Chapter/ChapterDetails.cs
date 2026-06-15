namespace TheDiscDb.Data.Changes.Chapter;

using TheDiscDb.Data.Changes.DiscFields;

/// <summary>
/// Payload for the <c>chapter.update</c> change type. Carries the editable
/// fields of a <see cref="TheDiscDb.InputModels.Chapter"/> plus the natural-key
/// composite that locates it inside its parent disc item.
/// </summary>
/// <remarks>
/// The Chapter entity hangs off <see cref="TheDiscDb.InputModels.DiscItemReference"/>
/// in the schema, but users address it as "chapter N of disc item M on disc K".
/// The resolver walks Disc → Title(Index=TitleIndex) → Item → Chapters and
/// matches on <see cref="ChapterIndex"/>. Chapters have no slug; index is the
/// only stable identifier inside their parent reference.
/// Index is identity, not editable. Only <see cref="Title"/> (chapter title
/// text) is mutable here.
/// </remarks>
public sealed record ChapterDetails(
    string? MediaItemSlug,
    string? BoxsetSlug,
    string ReleaseSlug,
    string? DiscSlug,
    int DiscIndex,
    int TitleIndex,
    int ChapterIndex,
    string? Title)
{
    /// <summary>
    /// Natural-key identifier: <c>"&lt;parent&gt;/&lt;release&gt;/&lt;disc&gt;/&lt;titleIndex&gt;/c&lt;chapterIndex&gt;"</c>.
    /// The <c>c</c> prefix keeps the chapter segment distinguishable from a
    /// future track segment in the same parent.
    /// </summary>
    public string TargetEntityKey =>
        $"{(this.MediaItemSlug ?? this.BoxsetSlug) ?? string.Empty}/{this.ReleaseSlug}/{DiscFieldsDetails.DiscToken(this.DiscSlug, this.DiscIndex)}/{this.TitleIndex.ToString(System.Globalization.CultureInfo.InvariantCulture)}/c{this.ChapterIndex.ToString(System.Globalization.CultureInfo.InvariantCulture)}";
}
