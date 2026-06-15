namespace TheDiscDb.Data.Changes.DiscItemFields;

using TheDiscDb.Data.Changes.DiscFields;

/// <summary>
/// Payload for the <c>disc-item.fields.update</c> change type. Carries the
/// editable fields of a <see cref="TheDiscDb.InputModels.Title"/> (the entity
/// type the rest of the data layer calls a "disc item") plus the linked
/// <see cref="TheDiscDb.InputModels.DiscItemReference"/>'s user-facing fields,
/// merged into a single payload for the user's mental model.
/// </summary>
/// <remarks>
/// Title and DiscItemReference are 1:1 in the schema
/// (<c>HasOne(x =&gt; x.Item).WithOne(x =&gt; x.DiscItem)</c>) so editing
/// "the disc item" can safely touch both without affecting other disc items.
/// Identity composite: release-parent + disc + <see cref="TitleIndex"/>.
/// Title has no slug, so the index is the only stable identifier within a disc.
/// </remarks>
public sealed record DiscItemFieldsDetails(
    string? MediaItemSlug,
    string? BoxsetSlug,
    string ReleaseSlug,
    string? DiscSlug,
    int DiscIndex,
    int TitleIndex,
    // Title direct fields
    string? Comment,
    string? SourceFile,
    string? SegmentMap,
    string? Duration,
    // Linked DiscItemReference fields. HasItem captures whether a reference
    // currently exists; on Apply a missing reference is created lazily when
    // any Item* field is non-null. This matches the schema's optional 1:1.
    bool HasItem,
    string? ItemTitle,
    string? ItemType,
    string? ItemDescription,
    string? ItemSeason,
    string? ItemEpisode)
{
    /// <summary>
    /// Natural-key identifier: <c>"&lt;parent&gt;/&lt;release&gt;/&lt;disc&gt;/&lt;titleIndex&gt;"</c>.
    /// </summary>
    public string TargetEntityKey =>
        $"{(this.MediaItemSlug ?? this.BoxsetSlug) ?? string.Empty}/{this.ReleaseSlug}/{DiscFieldsDetails.DiscToken(this.DiscSlug, this.DiscIndex)}/{this.TitleIndex.ToString(System.Globalization.CultureInfo.InvariantCulture)}";
}
