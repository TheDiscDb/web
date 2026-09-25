namespace TheDiscDb.OpticalDiscParsers.Bdmv.Models;

using TheDiscDb.OpticalDiscParsers.Diagnostics;

/// <summary>
/// Represents parsed information from a Blu-ray playlist (.mpls) file.
/// </summary>
public record MplsPlaylist
{
    /// <summary>Gets the file identifier string, always "MPLS" for playlist files.</summary>
    public required string Identifier { get; init; }

    /// <summary>Gets the BDMV format version string, such as "0200" or "0300".</summary>
    public required string Version { get; init; }

    /// <summary>Gets the byte offset where the PlayList section starts.</summary>
    public required uint PlaylistStartAddress { get; init; }

    /// <summary>Gets the byte offset where the PlayListMark section starts.</summary>
    public required uint PlaylistMarkStartAddress { get; init; }

    /// <summary>Gets the byte offset where extension data starts, or 0 when absent.</summary>
    public required uint ExtensionDataStartAddress { get; init; }

    /// <summary>Gets AppInfoPlayList metadata.</summary>
    public required MplsAppInfo AppInfo { get; init; }

    /// <summary>Gets primary playlist play items.</summary>
    public required IReadOnlyList<MplsPlayItem> PlayItems { get; init; }

    /// <summary>Gets subpath summaries.</summary>
    public required IReadOnlyList<MplsSubPath> SubPaths { get; init; }

    /// <summary>Gets playlist marks, usually chapters.</summary>
    public required IReadOnlyList<MplsPlaylistMark> Marks { get; init; }

    /// <summary>Gets generic MPLS extension-data metadata when present.</summary>
    public MplsExtensionData? ExtensionData { get; init; }

    /// <summary>Gets subpaths declared in MPLS extension data.</summary>
    public required IReadOnlyList<MplsSubPath> ExtensionSubPaths { get; init; }

    /// <summary>Gets explicit stereoscopic base/dependent-view relationships.</summary>
    public required IReadOnlyList<MplsStereoVideoRelationship> StereoVideoRelationships { get; init; }

    /// <summary>Gets diagnostics generated while parsing.</summary>
    public required IReadOnlyList<ParserDiagnostic> Diagnostics { get; init; }
}

/// <summary>
/// Represents the generic MPLS ExtensionData container.
/// </summary>
public record MplsExtensionData
{
    /// <summary>Gets the declared extension-data length, excluding the length field.</summary>
    public required uint Length { get; init; }

    /// <summary>Gets the relative start address of the extension data block.</summary>
    public required uint DataBlockStartAddress { get; init; }

    /// <summary>Gets extension-data entry descriptors.</summary>
    public required IReadOnlyList<MplsExtensionDataEntry> Entries { get; init; }
}

/// <summary>
/// Represents one MPLS ExtensionData entry descriptor.
/// </summary>
public record MplsExtensionDataEntry
{
    /// <summary>Gets the entry index.</summary>
    public required int Index { get; init; }

    /// <summary>Gets the first extension type identifier.</summary>
    public required ushort TypeIdentifier { get; init; }

    /// <summary>Gets the second extension type/version identifier.</summary>
    public required ushort VersionIdentifier { get; init; }

    /// <summary>Gets the entry start address relative to the ExtensionData start.</summary>
    public required uint RelativeStartAddress { get; init; }

    /// <summary>Gets the declared entry length.</summary>
    public required uint Length { get; init; }

    /// <summary>Gets a parser-supported entry name when known.</summary>
    public string? Name { get; init; }

    /// <summary>Gets whether this entry was parsed into a typed model.</summary>
    public required bool IsSupported { get; init; }

    /// <summary>Gets whether this entry overlaps another descriptor range.</summary>
    public required bool OverlapsAnotherEntry { get; init; }

    /// <summary>Gets bounded raw entry data as uppercase hexadecimal for unsupported entries.</summary>
    public string? RawDataHex { get; init; }

    /// <summary>Gets whether <see cref="RawDataHex"/> was truncated for size.</summary>
    public required bool IsRawDataTruncated { get; init; }
}

/// <summary>
/// Represents AppInfoPlayList metadata.
/// </summary>
public record MplsAppInfo
{
    /// <summary>Gets the declared AppInfoPlayList length.</summary>
    public required uint Length { get; init; }

    /// <summary>Gets the raw playback type.</summary>
    public required int PlaybackTypeCode { get; init; }

    /// <summary>Gets the playback type name when known.</summary>
    public required string PlaybackType { get; init; }

    /// <summary>Gets the playback count for random/shuffle playback, when present.</summary>
    public int? PlaybackCount { get; init; }

    /// <summary>Gets whether random access is allowed.</summary>
    public required bool RandomAccessFlag { get; init; }

    /// <summary>Gets whether audio mixing is requested.</summary>
    public required bool AudioMixFlag { get; init; }

    /// <summary>Gets whether lossless bypass is requested.</summary>
    public required bool LosslessBypassFlag { get; init; }

    /// <summary>Gets whether MVC base view R flag is set.</summary>
    public required bool MvcBaseViewRFlag { get; init; }
}

/// <summary>
/// Represents one PlayItem in a playlist.
/// </summary>
public record MplsPlayItem
{
    /// <summary>Gets the play item index.</summary>
    public required int Index { get; init; }

    /// <summary>Gets the primary clip identifier, e.g. "00001".</summary>
    public required string ClipId { get; init; }

    /// <summary>Gets the primary clip codec identifier, typically "M2TS".</summary>
    public required string CodecId { get; init; }

    /// <summary>Gets the connection condition code.</summary>
    public required int ConnectionCondition { get; init; }

    /// <summary>Gets whether this play item has multiple angles.</summary>
    public required bool IsMultiAngle { get; init; }

    /// <summary>Gets the STC id for the primary clip.</summary>
    public required int StcId { get; init; }

    /// <summary>Gets the in-time in 45 kHz ticks.</summary>
    public required uint InTime { get; init; }

    /// <summary>Gets the out-time in 45 kHz ticks.</summary>
    public required uint OutTime { get; init; }

    /// <summary>Gets the play item duration.</summary>
    public TimeSpan Duration => TimeSpan.FromSeconds((OutTime - InTime) / 45000.0);

    /// <summary>Gets whether random access is allowed.</summary>
    public required bool RandomAccessFlag { get; init; }

    /// <summary>Gets the still mode code.</summary>
    public required int StillMode { get; init; }

    /// <summary>Gets the still time, when present.</summary>
    public int? StillTime { get; init; }

    /// <summary>Gets alternate angle clips, including the primary clip.</summary>
    public required IReadOnlyList<MplsClipReference> Clips { get; init; }

    /// <summary>Gets stream table metadata for this play item.</summary>
    public required MplsStreamTable StreamTable { get; init; }
}

/// <summary>
/// Represents a clip reference in a PlayItem or SubPlayItem.
/// </summary>
public record MplsClipReference
{
    /// <summary>Gets the clip identifier, e.g. "00001".</summary>
    public required string ClipId { get; init; }

    /// <summary>Gets the clip codec identifier, typically "M2TS".</summary>
    public required string CodecId { get; init; }

    /// <summary>Gets the STC id.</summary>
    public required int StcId { get; init; }
}

/// <summary>
/// Represents stream-number table metadata for one PlayItem.
/// </summary>
public record MplsStreamTable
{
    /// <summary>Gets primary video streams.</summary>
    public required IReadOnlyList<MplsStream> VideoStreams { get; init; }

    /// <summary>Gets primary audio streams.</summary>
    public required IReadOnlyList<MplsStream> AudioStreams { get; init; }

    /// <summary>Gets presentation graphics streams.</summary>
    public required IReadOnlyList<MplsStream> PresentationGraphicsStreams { get; init; }

    /// <summary>Gets interactive graphics streams.</summary>
    public required IReadOnlyList<MplsStream> InteractiveGraphicsStreams { get; init; }

    /// <summary>Gets secondary audio streams.</summary>
    public required IReadOnlyList<MplsStream> SecondaryAudioStreams { get; init; }

    /// <summary>Gets secondary video streams.</summary>
    public required IReadOnlyList<MplsStream> SecondaryVideoStreams { get; init; }
}

/// <summary>
/// Represents one stream entry in an MPLS STN table.
/// </summary>
public record MplsStream
{
    /// <summary>Gets the stream category within the STN table.</summary>
    public required string Category { get; init; }

    /// <summary>Gets the raw stream type code.</summary>
    public required int StreamTypeCode { get; init; }

    /// <summary>Gets the transport stream PID.</summary>
    public ushort? Pid { get; init; }

    /// <summary>Gets the referenced subpath id when present.</summary>
    public int? SubPathId { get; init; }

    /// <summary>Gets the referenced subclip id when present.</summary>
    public int? SubClipId { get; init; }

    /// <summary>Gets the raw stream coding type.</summary>
    public required int CodingTypeCode { get; init; }

    /// <summary>Gets the stream coding type name when known.</summary>
    public required string CodingType { get; init; }

    /// <summary>Gets the raw format code when present.</summary>
    public int? FormatCode { get; init; }

    /// <summary>Gets the raw rate code when present.</summary>
    public int? RateCode { get; init; }

    /// <summary>Gets the language code when present.</summary>
    public string? LanguageCode { get; init; }

    /// <summary>Gets the character-code field for text subtitles when present.</summary>
    public int? CharacterCode { get; init; }
}

/// <summary>
/// Represents an SS/MVC dependent-view stream declared by MPLS extension data.
/// </summary>
public record MplsStereoVideoStream
{
    /// <summary>Gets the primary PlayItem index this extension record belongs to.</summary>
    public required int PlayItemIndex { get; init; }

    /// <summary>Gets the primary PlayItem video-stream index this extension record augments.</summary>
    public required int VideoStreamIndex { get; init; }

    /// <summary>Gets the raw stream-entry type code.</summary>
    public required int StreamTypeCode { get; init; }

    /// <summary>Gets the referenced transport stream PID when declared.</summary>
    public ushort? Pid { get; init; }

    /// <summary>Gets the referenced extension subpath id when declared.</summary>
    public int? SubPathId { get; init; }

    /// <summary>Gets the referenced subclip id when declared.</summary>
    public int? SubClipId { get; init; }

    /// <summary>Gets the raw coding type from stream_attributes_SS.</summary>
    public required int CodingTypeCode { get; init; }

    /// <summary>Gets the coding type name when known.</summary>
    public required string CodingType { get; init; }

    /// <summary>Gets the raw format code when present.</summary>
    public int? FormatCode { get; init; }

    /// <summary>Gets the raw rate code when present.</summary>
    public int? RateCode { get; init; }

    /// <summary>Gets the declared number of offset sequences.</summary>
    public int? NumberOfOffsetSequences { get; init; }

    /// <summary>Gets raw SS stream-attribute bytes as uppercase hexadecimal.</summary>
    public required string RawAttributesHex { get; init; }
}

/// <summary>
/// Represents a playlist subpath summary.
/// </summary>
public record MplsSubPath
{
    /// <summary>Gets the subpath index.</summary>
    public required int Index { get; init; }

    /// <summary>Gets the raw subpath type.</summary>
    public required int Type { get; init; }

    /// <summary>Gets whether this subpath repeats.</summary>
    public required bool IsRepeat { get; init; }

    /// <summary>Gets the number of SubPlayItems in this subpath.</summary>
    public required int SubPlayItemCount { get; init; }

    /// <summary>Gets SubPlayItem records for this subpath.</summary>
    public required IReadOnlyList<MplsSubPlayItem> SubPlayItems { get; init; }
}

/// <summary>
/// Represents one SubPlayItem in an MPLS subpath.
/// </summary>
public record MplsSubPlayItem
{
    /// <summary>Gets the subplay item index within the subpath.</summary>
    public required int Index { get; init; }

    /// <summary>Gets the primary subpath clip identifier.</summary>
    public required string ClipId { get; init; }

    /// <summary>Gets the primary subpath codec identifier.</summary>
    public required string CodecId { get; init; }

    /// <summary>Gets the connection condition code.</summary>
    public required int ConnectionCondition { get; init; }

    /// <summary>Gets whether this SubPlayItem references multiple clips.</summary>
    public required bool IsMultiClip { get; init; }

    /// <summary>Gets the STC id for the primary clip.</summary>
    public required int StcId { get; init; }

    /// <summary>Gets the in-time in 45 kHz ticks.</summary>
    public required uint InTime { get; init; }

    /// <summary>Gets the out-time in 45 kHz ticks.</summary>
    public required uint OutTime { get; init; }

    /// <summary>Gets the synchronized primary play-item index.</summary>
    public required int SyncPlayItemId { get; init; }

    /// <summary>Gets the synchronization timestamp in 45 kHz ticks.</summary>
    public required uint SyncPresentationTimestamp { get; init; }

    /// <summary>Gets referenced clips, including the primary clip.</summary>
    public required IReadOnlyList<MplsClipReference> Clips { get; init; }
}

/// <summary>
/// Represents an explicit 3D base/dependent-view relationship from MPLS extension data.
/// </summary>
public record MplsStereoVideoRelationship
{
    /// <summary>Relationship type for MVC dependent views.</summary>
    public const string ThreeDimensionalDependentView = "3d-dependent-view";

    /// <summary>Gets the relationship type.</summary>
    public required string RelationshipType { get; init; }

    /// <summary>Gets the base-view PlayItem index.</summary>
    public required int BasePlayItemIndex { get; init; }

    /// <summary>Gets the base-view clip id.</summary>
    public required string BaseClipId { get; init; }

    /// <summary>Gets the base-view codec id.</summary>
    public required string BaseCodecId { get; init; }

    /// <summary>Gets the base-view STC id.</summary>
    public required int BaseStcId { get; init; }

    /// <summary>Gets the extension SubPath index for the dependent view.</summary>
    public required int DependentSubPathIndex { get; init; }

    /// <summary>Gets the extension SubPath type code.</summary>
    public required int DependentSubPathType { get; init; }

    /// <summary>Gets the extension SubPlayItem index for the dependent view.</summary>
    public required int DependentSubPlayItemIndex { get; init; }

    /// <summary>Gets the dependent-view clip id.</summary>
    public required string DependentClipId { get; init; }

    /// <summary>Gets the dependent-view codec id.</summary>
    public required string DependentCodecId { get; init; }

    /// <summary>Gets the dependent-view STC id.</summary>
    public required int DependentStcId { get; init; }

    /// <summary>Gets the synchronization PlayItem id declared by the SubPlayItem.</summary>
    public required int SyncPlayItemId { get; init; }

    /// <summary>Gets the synchronization timestamp declared by the SubPlayItem.</summary>
    public required uint SyncPresentationTimestamp { get; init; }

    /// <summary>Gets whether the relationship is declared through an SS Video subpath.</summary>
    public required bool IsSsVideoSubPath { get; init; }

    /// <summary>Gets dependent-view stream metadata when declared by the STN SS extension.</summary>
    public MplsStereoVideoStream? DependentViewStream { get; init; }
}

/// <summary>
/// Represents one PlayListMark entry.
/// </summary>
public record MplsPlaylistMark
{
    /// <summary>Mark type for an entry/chapter mark.</summary>
    public const int EntryMarkType = 0x01;

    /// <summary>Mark type for a link mark.</summary>
    public const int LinkMarkType = 0x02;

    /// <summary>Gets the mark index.</summary>
    public required int Index { get; init; }

    /// <summary>Gets the raw mark type.</summary>
    public required int MarkType { get; init; }

    /// <summary>Gets the referenced play item index.</summary>
    public required int PlayItemReference { get; init; }

    /// <summary>Gets the mark timestamp in 45 kHz ticks.</summary>
    public required uint Time { get; init; }

    /// <summary>Gets the referenced entry ES PID.</summary>
    public required ushort EntryElementaryStreamPid { get; init; }

    /// <summary>Gets the mark duration in 45 kHz ticks.</summary>
    public required uint Duration { get; init; }

    /// <summary>Gets the mark timestamp as a TimeSpan.</summary>
    public TimeSpan TimeSpan => TimeSpan.FromSeconds(Time / 45000.0);

    /// <summary>Gets whether this mark is an entry/chapter mark.</summary>
    public bool IsEntryMark => MarkType == EntryMarkType;

    /// <summary>Gets whether this mark is a link mark.</summary>
    public bool IsLinkMark => MarkType == LinkMarkType;
}
