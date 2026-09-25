using System.Text.Json.Serialization;

namespace TheDiscDb.OpticalDiscManifest.Models;

public sealed record OpticalDiscManifestDocument
{
    [JsonPropertyName("$schema")]
    [JsonPropertyOrder(-100)]
    public required string Schema { get; init; }

    [JsonPropertyOrder(-90)]
    public required int SchemaVersion { get; init; }

    [JsonPropertyOrder(-80)]
    public required ManifestProducer Producer { get; init; }

    [JsonPropertyOrder(-70)]
    public required IReadOnlyList<string> Capabilities { get; init; }

    [JsonPropertyOrder(-60)]
    public required ManifestDisc Disc { get; init; }

    [JsonPropertyOrder(-50)]
    public IReadOnlyList<ManifestDiagnostic>? Diagnostics { get; init; }
}

public sealed record ManifestProducer
{
    public required string Name { get; init; }

    public required string Version { get; init; }

    public string? Uri { get; init; }
}

public sealed record ManifestDisc
{
    public required string Format { get; init; }

    public string? Name { get; init; }

    public IReadOnlyList<ManifestIdentifier>? Identifiers { get; init; }

    public required IReadOnlyList<ManifestFile> Files { get; init; }

    public IReadOnlyList<ManifestTitle>? Titles { get; init; }

    /// <summary>
    /// Gets CLPI-backed standalone clip candidates observed on the disc. These are
    /// distinct from <see cref="Titles"/>: a clip records authored clip-information
    /// evidence (path identity, exact stream-file size, CLPI presentation timing, and
    /// authored stream evidence) without asserting that the clip is a playable logical
    /// title or playlist.
    /// </summary>
    public IReadOnlyList<ManifestClip>? Clips { get; init; }
}

public sealed record ManifestIdentifier
{
    public required string Kind { get; init; }

    public required string Value { get; init; }

    public required string ComputedBy { get; init; }

    public string? AlgorithmVersion { get; init; }

    public IReadOnlyList<string>? InputPaths { get; init; }
}

public sealed record ManifestFile
{
    public required string Path { get; init; }

    public required long SizeBytes { get; init; }

    public required string Role { get; init; }
}

public sealed record ManifestTitle
{
    public required int Index { get; init; }

    public required ManifestTitleSource Source { get; init; }

    public double? DurationSeconds { get; init; }

    public long? DurationTicks45k { get; init; }

    /// <summary>
    /// Gets the observed size, in bytes, of the stream file(s) backing this title. For a
    /// stereoscopic 3D title this is the sum of the base-view clip and the dependent MVC
    /// clip file sizes when both are known. This is derived only from file-size metadata
    /// (never by reading M2TS/SSIF payload bytes) and is a best-effort partial sum when
    /// only some backing clip files are present in the file inventory.
    /// </summary>
    public long? SizeBytes { get; init; }

    public int? ChapterCount { get; init; }

    public IReadOnlyList<ManifestChapter>? Chapters { get; init; }

    public IReadOnlyList<ManifestSegment>? Segments { get; init; }

    public IReadOnlyList<ManifestStream>? Streams { get; init; }

    /// <summary>
    /// Gets the explicit base-view/dependent-view stereoscopic 3D relationship for this
    /// title when the playlist declares one (Blu-ray 3D MVC). This represents a semantic
    /// base+dependent-view pairing, not an alternate camera angle and not a separate
    /// logical title.
    /// </summary>
    public ManifestStereoscopicView? Stereoscopic3D { get; init; }
}

public sealed record ManifestTitleSource
{
    public required string Kind { get; init; }

    public string? Path { get; init; }

    public int? TitleSet { get; init; }

    public int? Title { get; init; }

    public int? TitleSetTitle { get; init; }

    public int? Pgc { get; init; }

    public int? Angle { get; init; }

    public string? Label { get; init; }
}

public sealed record ManifestChapter
{
    public required int Index { get; init; }

    public double? StartSeconds { get; init; }

    public double? DurationSeconds { get; init; }

    public long? StartTicks45k { get; init; }

    public long? DurationTicks45k { get; init; }
}

public sealed record ManifestSegment
{
    public required int Index { get; init; }

    public required string Clip { get; init; }

    public string? FilePath { get; init; }

    public double? StartSeconds { get; init; }

    public double? DurationSeconds { get; init; }

    public long? StartTicks45k { get; init; }

    public long? DurationTicks45k { get; init; }

    public int? Angle { get; init; }
}

public sealed record ManifestStream
{
    public required int Index { get; init; }

    public required string Type { get; init; }

    public required string Codec { get; init; }

    public int? Pid { get; init; }

    public int? CodingTypeCode { get; init; }

    public int? FormatCode { get; init; }

    public int? RateCode { get; init; }

    /// <summary>
    /// Gets the raw HEVC dynamic-range type code from the CLPI STN table extension,
    /// when present. This is control-file evidence only (e.g. SDR/HDR10/Dolby Vision
    /// signaling per the BD-ROM HEVC stream-coding extension); it never reflects
    /// payload-inspected profile/level or RPU details.
    /// </summary>
    public int? DynamicRangeTypeCode { get; init; }

    /// <summary>
    /// Gets the raw HEVC color-space code from the CLPI STN table extension, when
    /// present. Control-file evidence only.
    /// </summary>
    public int? ColorSpaceCode { get; init; }

    /// <summary>
    /// Gets whether the HEVC stream sets the HDR10+ flag in the CLPI STN table
    /// extension, when present. Control-file evidence only.
    /// </summary>
    public bool? HdrPlusFlag { get; init; }

    /// <summary>
    /// Gets the DVD line-21 closed-caption fields (1 and/or 2) that VTS_V_ATR declares for a
    /// video stream. This is IFO authoring evidence only; caption payload user-data is never read,
    /// so it does not prove that caption data is actually present.
    /// </summary>
    public IReadOnlyList<int>? Line21ClosedCaptionFields { get; init; }

    public string? Language { get; init; }

    public string? LanguageName { get; init; }

    public string? AudioLayout { get; init; }

    public string? Resolution { get; init; }

    public string? AspectRatio { get; init; }
}

public sealed record ManifestStereoscopicView
{
    /// <summary>
    /// Gets the relationship type. Always <c>3d-dependent-view</c>: an explicit base-view
    /// + MVC dependent-view pairing, never an alternate camera angle.
    /// </summary>
    public required string RelationshipType { get; init; }

    public required string BaseClipId { get; init; }

    public string? BaseCodecId { get; init; }

    public int? BaseStcId { get; init; }

    public required string DependentClipId { get; init; }

    public string? DependentCodecId { get; init; }

    public int? DependentStcId { get; init; }

    public int? SyncPlayItemId { get; init; }

    public long? SyncPresentationTimestampTicks45k { get; init; }

    public bool? IsSsVideoSubPath { get; init; }

    public ManifestStereoscopicDependentStream? DependentStream { get; init; }
}

public sealed record ManifestStereoscopicDependentStream
{
    public int? Pid { get; init; }

    public required int CodingTypeCode { get; init; }

    public required string Codec { get; init; }

    public int? FormatCode { get; init; }

    public int? RateCode { get; init; }
}

/// <summary>
/// Represents a CLPI-backed standalone clip candidate: a <c>BDMV/STREAM/xxxxx.m2ts</c>
/// stream file paired by five-character stem with its <c>BDMV/CLIPINF/xxxxx.clpi</c>
/// clip-information file, when present. This is semantically distinct from
/// <see cref="ManifestTitle"/>: it records authored clip-level evidence without
/// asserting the clip is a playable logical title or playlist candidate.
/// </summary>
public sealed record ManifestClip
{
    public required string ClipId { get; init; }

    public string? StreamPath { get; init; }

    /// <summary>Gets the exact observed M2TS stream-file size, from file metadata only.</summary>
    public long? SizeBytes { get; init; }

    public string? ClipInfoPath { get; init; }

    public double? DurationSeconds { get; init; }

    public long? DurationTicks45k { get; init; }

    public long? NumberOfSourcePackets { get; init; }

    public long? TransportStreamRecordingRate { get; init; }

    public IReadOnlyList<ManifestStream>? Streams { get; init; }
}

public sealed record ManifestDiagnostic
{
    public required string Severity { get; init; }

    public required string Code { get; init; }

    public required string Message { get; init; }

    public string? Path { get; init; }

    public long? ByteOffset { get; init; }
}

public sealed record ManifestScanMetrics
{
    public required int FilesEnumerated { get; init; }

    public required int ControlFilesRead { get; init; }

    public required long ControlBytesRead { get; init; }

    public required long OutputBytes { get; init; }

    public required double ElapsedMilliseconds { get; init; }

    public required long ManagedMemoryBeforeBytes { get; init; }

    public required long ManagedMemoryAfterBytes { get; init; }
}

public sealed record ManifestValidationResult(
    bool IsValid,
    IReadOnlyList<string> Errors);

public sealed record ManifestGenerationResult
{
    public required OpticalDiscManifestDocument Manifest { get; init; }

    public required byte[] Json { get; init; }

    public required ManifestValidationResult Validation { get; init; }

    public required ManifestScanMetrics Metrics { get; init; }
}
