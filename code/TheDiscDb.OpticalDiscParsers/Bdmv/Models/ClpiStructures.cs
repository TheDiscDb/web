namespace TheDiscDb.OpticalDiscParsers.Bdmv.Models;

using TheDiscDb.OpticalDiscParsers.Diagnostics;

/// <summary>
/// Represents parsed information from a Blu-ray clip information (.clpi) file.
/// </summary>
public record ClpiFile
{
    /// <summary>Gets the file identifier string, always "HDMV" for CLPI files.</summary>
    public required string Identifier { get; init; }

    /// <summary>Gets the BDMV format version string, such as "0200" or "0300".</summary>
    public required string Version { get; init; }

    /// <summary>Gets the byte offset where SequenceInfo starts.</summary>
    public required uint SequenceInfoStartAddress { get; init; }

    /// <summary>Gets the byte offset where ProgramInfo starts.</summary>
    public required uint ProgramInfoStartAddress { get; init; }

    /// <summary>Gets the byte offset where CPI starts.</summary>
    public required uint CpiStartAddress { get; init; }

    /// <summary>Gets the byte offset where ClipMark starts.</summary>
    public required uint ClipMarkStartAddress { get; init; }

    /// <summary>Gets the byte offset where extension data starts, or 0 when absent.</summary>
    public required uint ExtensionDataStartAddress { get; init; }

    /// <summary>Gets the ClipInfo block.</summary>
    public required ClpiClipInfo ClipInfo { get; init; }

    /// <summary>Gets ATC sequence entries from SequenceInfo.</summary>
    public required IReadOnlyList<ClpiAtcSequence> AtcSequences { get; init; }

    /// <summary>Gets programs from ProgramInfo.</summary>
    public required IReadOnlyList<ClpiProgram> Programs { get; init; }

    /// <summary>Gets stream declarations from CLPI extension data.</summary>
    public required IReadOnlyList<ClpiProgramStream> ExtensionStreams { get; init; }

    /// <summary>Gets CPI entry-point-map summaries.</summary>
    public required IReadOnlyList<ClpiCpiEntry> CpiEntries { get; init; }

    /// <summary>Gets presentation timing summarized from SequenceInfo STC records.</summary>
    public ClpiPresentationSummary? PresentationSummary { get; init; }

    /// <summary>Gets diagnostics generated while parsing.</summary>
    public required IReadOnlyList<ParserDiagnostic> Diagnostics { get; init; }
}

/// <summary>
/// Authored presentation timing summarized from CLPI SequenceInfo.
/// </summary>
public record ClpiPresentationSummary
{
    /// <summary>Gets the earliest presentation start time in 45 kHz ticks.</summary>
    public required uint StartTime { get; init; }

    /// <summary>Gets the latest presentation end time in 45 kHz ticks.</summary>
    public required uint EndTime { get; init; }

    /// <summary>Gets the authored duration in 45 kHz ticks.</summary>
    public required uint DurationTicks45k { get; init; }

    /// <summary>Gets the authored duration as a TimeSpan.</summary>
    public TimeSpan Duration => TimeSpan.FromSeconds(DurationTicks45k / 45000.0);

    /// <summary>Gets the number of STC sequences contributing to the summary.</summary>
    public required int StcSequenceCount { get; init; }
}

/// <summary>
/// Represents CLPI ClipInfo metadata.
/// </summary>
public record ClpiClipInfo
{
    /// <summary>Gets the declared ClipInfo length.</summary>
    public required uint Length { get; init; }

    /// <summary>Gets the raw clip stream type.</summary>
    public required int ClipStreamType { get; init; }

    /// <summary>Gets the raw application type.</summary>
    public required int ApplicationType { get; init; }

    /// <summary>Gets whether ATC delta entries are present.</summary>
    public required bool IsAtcDelta { get; init; }

    /// <summary>Gets the transport-stream recording rate.</summary>
    public required uint TransportStreamRecordingRate { get; init; }

    /// <summary>Gets the source packet count.</summary>
    public required uint NumberOfSourcePackets { get; init; }

    /// <summary>Gets TS type validity when present.</summary>
    public byte? TsTypeValidity { get; init; }

    /// <summary>Gets the TS type format identifier when present.</summary>
    public string? TsTypeFormatIdentifier { get; init; }
}

/// <summary>
/// Represents one ATC sequence from SequenceInfo.
/// </summary>
public record ClpiAtcSequence
{
    /// <summary>Gets the ATC sequence index.</summary>
    public required int Index { get; init; }

    /// <summary>Gets the source packet number where the ATC sequence starts.</summary>
    public required uint SourcePacketNumberAtcStart { get; init; }

    /// <summary>Gets the STC ID offset for this ATC sequence.</summary>
    public required int OffsetStcId { get; init; }

    /// <summary>Gets STC sequences in this ATC sequence.</summary>
    public required IReadOnlyList<ClpiStcSequence> StcSequences { get; init; }
}

/// <summary>
/// Represents one STC sequence from SequenceInfo.
/// </summary>
public record ClpiStcSequence
{
    /// <summary>Gets the STC sequence index within the ATC sequence.</summary>
    public required int Index { get; init; }

    /// <summary>Gets the PCR PID.</summary>
    public required ushort PcrPid { get; init; }

    /// <summary>Gets the source packet number where this STC sequence starts.</summary>
    public required uint SourcePacketNumberStcStart { get; init; }

    /// <summary>Gets the presentation start time in 45 kHz ticks.</summary>
    public required uint PresentationStartTime { get; init; }

    /// <summary>Gets the presentation end time in 45 kHz ticks.</summary>
    public required uint PresentationEndTime { get; init; }

    /// <summary>Gets the presentation duration.</summary>
    public TimeSpan Duration => TimeSpan.FromSeconds((PresentationEndTime - PresentationStartTime) / 45000.0);
}

/// <summary>
/// Represents one ProgramInfo program.
/// </summary>
public record ClpiProgram
{
    /// <summary>Gets the program index.</summary>
    public required int Index { get; init; }

    /// <summary>Gets the source packet number where this program sequence starts.</summary>
    public required uint SourcePacketNumberProgramSequenceStart { get; init; }

    /// <summary>Gets the PMT PID.</summary>
    public required ushort ProgramMapPid { get; init; }

    /// <summary>Gets the number of stream groups.</summary>
    public required int NumberOfGroups { get; init; }

    /// <summary>Gets program stream entries.</summary>
    public required IReadOnlyList<ClpiProgramStream> Streams { get; init; }
}

/// <summary>
/// Represents one stream entry in ProgramInfo.
/// </summary>
public record ClpiProgramStream
{
    /// <summary>Gets the broad stream category inferred from the coding type.</summary>
    public required string Category { get; init; }

    /// <summary>Gets the transport stream PID.</summary>
    public required ushort Pid { get; init; }

    /// <summary>Gets the declared stream-attribute byte length.</summary>
    public required int AttributeLength { get; init; }

    /// <summary>Gets the raw stream-attribute bytes as uppercase hexadecimal.</summary>
    public required string RawAttributesHex { get; init; }

    /// <summary>Gets the raw stream coding type.</summary>
    public required int CodingTypeCode { get; init; }

    /// <summary>Gets the stream coding type name when known.</summary>
    public required string CodingType { get; init; }

    /// <summary>Gets the raw format code when present.</summary>
    public int? FormatCode { get; init; }

    /// <summary>Gets the raw rate code when present.</summary>
    public int? RateCode { get; init; }

    /// <summary>Gets the raw aspect-ratio code when present.</summary>
    public int? AspectCode { get; init; }

    /// <summary>Gets whether the video stream sets the OC flag.</summary>
    public bool? OcFlag { get; init; }

    /// <summary>Gets whether the HEVC stream sets the CR flag.</summary>
    public bool? CrFlag { get; init; }

    /// <summary>Gets the raw HEVC dynamic-range type code when present.</summary>
    public int? DynamicRangeTypeCode { get; init; }

    /// <summary>Gets the raw HEVC color-space code when present.</summary>
    public int? ColorSpaceCode { get; init; }

    /// <summary>Gets whether the HEVC stream sets the HDR10+ flag.</summary>
    public bool? HdrPlusFlag { get; init; }

    /// <summary>Gets a language code when present.</summary>
    public string? LanguageCode { get; init; }

    /// <summary>Gets the character-code field for text subtitles when present.</summary>
    public int? CharacterCode { get; init; }

    /// <summary>Gets the ISRC field when present and non-empty.</summary>
    public string? InternationalStandardRecordingCode { get; init; }
}

/// <summary>
/// Represents one CPI entry-point-map stream summary.
/// </summary>
public record ClpiCpiEntry
{
    /// <summary>Gets the transport stream PID for the entry map.</summary>
    public required ushort Pid { get; init; }

    /// <summary>Gets the raw EP stream type.</summary>
    public required int EntryPointStreamType { get; init; }

    /// <summary>Gets the number of coarse entry-point records.</summary>
    public required int NumberOfCoarseEntries { get; init; }

    /// <summary>Gets the number of fine entry-point records.</summary>
    public required int NumberOfFineEntries { get; init; }

    /// <summary>Gets the absolute byte offset where the EP map stream starts.</summary>
    public required uint EntryPointMapStreamStartAddress { get; init; }
}
