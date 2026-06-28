namespace TheDiscDb.Data.Changes.Track;

/// <summary>
/// Payload for the <c>track.fields.update</c> change type. Carries the editable
/// fields of a <see cref="TheDiscDb.InputModels.Track"/>. Despite the original
/// design doc's "AudioTrack" naming this type handles all track kinds (video,
/// audio, subtitle) because the underlying entity is a single discriminated row.
/// </summary>
/// <remarks>
/// Identity composite: release-parent + disc-slug + title-index + track-index.
/// Track has no slug — Index within its parent Title is the stable identifier.
/// Parent disc is identified by <see cref="DiscSlug"/>; slug-less discs are
/// not addressable through this change type by design.
/// <see cref="Type"/> (Video/Audio/Subtitle) is editable: users may correct a
/// misclassified track. Video-only and audio-only fields are all carried so a
/// single change type can handle re-typing.
/// </remarks>
public sealed record TrackFieldsDetails(
    string? MediaItemSlug,
    string? BoxsetSlug,
    string ReleaseSlug,
    string DiscSlug,
    int TitleIndex,
    int TrackIndex,
    string? Name,
    string? Type,
    string? Resolution,
    string? AspectRatio,
    string? AudioType,
    string? LanguageCode,
    string? Language,
    string? Description)
{
    /// <summary>
    /// Natural-key identifier: <c>"&lt;parent&gt;/&lt;release&gt;/&lt;discSlug&gt;/&lt;titleIndex&gt;/t&lt;trackIndex&gt;"</c>.
    /// </summary>
    public string TargetEntityKey =>
        $"{(this.MediaItemSlug ?? this.BoxsetSlug) ?? string.Empty}/{this.ReleaseSlug}/{this.DiscSlug}/{this.TitleIndex.ToString(System.Globalization.CultureInfo.InvariantCulture)}/t{this.TrackIndex.ToString(System.Globalization.CultureInfo.InvariantCulture)}";
}
