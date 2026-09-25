namespace TheDiscDb.OpticalDiscParsers.Dvd.Models;

using TheDiscDb.OpticalDiscParsers.Input;
using TheDiscDb.OpticalDiscParsers.Models;

/// <summary>
/// Represents parsed information from a DVD Video Manager Information (VMGI) file.
/// Contains disc-level metadata, region codes, and references to title sets.
/// </summary>
public record VmgiHeader
{
    /// <summary>Gets the identifier string from the VMGI file (always "DVDVIDEO-VMG").</summary>
    public required string Identifier { get; init; }

    /// <summary>Gets the DVD specification version (typically 0x0110 for version 1.1).</summary>
    public required ushort SpecificationVersion { get; init; }

    /// <summary>Gets the provider identifier (studio/distributor name, up to 32 bytes).</summary>
    public required string ProviderId { get; init; }

    /// <summary>Gets the last sector address of this VMGI structure.</summary>
    public required uint LastSectorAddress { get; init; }

    /// <summary>Gets the number of volume titles (title sets) on this disc.</summary>
    public required int NumberOfVolumeTitles { get; init; }

    /// <summary>Gets the number of title sets referenced by the VMGI header.</summary>
    public required int NumberOfTitleSets { get; init; }

    /// <summary>Gets the number of VMG menus.</summary>
    public required int NumberOfVmgMenus { get; init; }

    /// <summary>Gets the regional coding restrictions (bitmask; 0xFF = all regions).</summary>
    public required byte RegionMask { get; init; }

    /// <summary>Gets the number of menus with VMG-specific accessibility features.</summary>
    public required int NumberOfAccessibleMenus { get; init; }

    /// <summary>Gets references to title set headers for this disc.</summary>
    public required IReadOnlyList<TitleSetReference> TitleSets { get; init; }

    /// <summary>Gets logical DVD title entries from the VMGI TT_SRPT table.</summary>
    public required IReadOnlyList<VmgiTitle> Titles { get; init; }

}

/// <summary>
/// Reference to a title set from the VMGI file.
/// </summary>
public record TitleSetReference
{
    /// <summary>Gets the title set number (1-based).</summary>
    public required int Number { get; init; }

    /// <summary>Gets the sector address where the VTSI for this title set starts.</summary>
    public required uint StartSectorAddress { get; init; }

    /// <summary>Gets the last sector address of this title set's VTSI structure.</summary>
    public required uint LastSectorAddress { get; init; }
}

/// <summary>
/// Represents a logical title entry from the VMGI TT_SRPT table.
/// </summary>
public record VmgiTitle
{
    /// <summary>Gets the logical title number (1-based).</summary>
    public required int Number { get; init; }

    /// <summary>Gets title playback behavior flags.</summary>
    public required DvdTitlePlaybackFlags PlaybackFlags { get; init; }

    /// <summary>Gets the number of angles declared for this title.</summary>
    public required int NumberOfAngles { get; init; }

    /// <summary>Gets the number of parts of title, commonly surfaced as chapters.</summary>
    public required int NumberOfPartsOfTitle { get; init; }

    /// <summary>Gets the parental management mask for this title.</summary>
    public required ushort ParentalManagementMask { get; init; }

    /// <summary>Gets the title set number (VTSN) containing this title.</summary>
    public required int TitleSetNumber { get; init; }

    /// <summary>Gets the title number within the referenced title set (VTS_TTN).</summary>
    public required int TitleSetTitleNumber { get; init; }

    /// <summary>Gets the title set start sector recorded in the VMGI title entry.</summary>
    public required uint TitleSetSector { get; init; }
}

/// <summary>
/// Playback flags from a VMGI TT_SRPT title entry.
/// </summary>
public record DvdTitlePlaybackFlags
{
    /// <summary>Gets the raw playback flag byte.</summary>
    public required byte RawValue { get; init; }

    /// <summary>Gets whether the title uses multiple or random PGC playback.</summary>
    public required bool IsMultiOrRandomPgcTitle { get; init; }

    /// <summary>Gets whether jump/link/call commands exist in cell commands.</summary>
    public required bool HasJumpLinkCallInCellCommand { get; init; }

    /// <summary>Gets whether jump/link/call commands exist in pre/post commands.</summary>
    public required bool HasJumpLinkCallInPrePostCommand { get; init; }

    /// <summary>Gets whether jump/link/call commands exist in button commands.</summary>
    public required bool HasJumpLinkCallInButtonCommand { get; init; }

    /// <summary>Gets whether jump/link/call commands exist in the title domain.</summary>
    public required bool HasJumpLinkCallInTitleDomain { get; init; }

    /// <summary>Gets whether chapter search or play user operations are permitted.</summary>
    public required bool ChapterSearchOrPlay { get; init; }

    /// <summary>Gets whether title or time play user operations are permitted.</summary>
    public required bool TitleOrTimePlay { get; init; }
}

/// <summary>
/// Represents parsed information from a DVD Video Title Set Information (VTSI) file.
/// Contains title structure, program chains, cells, and stream metadata.
/// </summary>
public record VtsiHeader
{
    /// <summary>Gets the identifier string from the VTSI file (format: "DVDVIDEO-VTS").</summary>
    public required string Identifier { get; init; }

    /// <summary>Gets the title set number this VTSI describes.</summary>
    public required int TitleSetNumber { get; init; }

    /// <summary>Gets the DVD specification version.</summary>
    public required ushort SpecificationVersion { get; init; }

    /// <summary>Gets the provider identifier from this title set.</summary>
    public required string ProviderId { get; init; }

    /// <summary>Gets the last sector address of this VTSI structure.</summary>
    public required uint LastSectorAddress { get; init; }

    /// <summary>Gets the number of program chains (titles) in this title set.</summary>
    public required int NumberOfProgramChains { get; init; }

    /// <summary>Gets the number of programs in this title set.</summary>
    public required int NumberOfPrograms { get; init; }

    /// <summary>Gets the number of cells in this title set.</summary>
    public required int NumberOfCells { get; init; }

    /// <summary>Gets the number of menus associated with this title set.</summary>
    public required int NumberOfMenus { get; init; }

    /// <summary>Gets information about video streams available in this title set.</summary>
    public required IReadOnlyList<VideoStreamInfo> VideoStreams { get; init; }

    /// <summary>Gets information about audio streams available in this title set.</summary>
    public required IReadOnlyList<AudioStreamInfo> AudioStreams { get; init; }

    /// <summary>Gets information about subtitle streams available in this title set.</summary>
    public required IReadOnlyList<SubtitleStreamInfo> SubtitleStreams { get; init; }

    /// <summary>Gets program chain information for each title in this title set.</summary>
    public required IReadOnlyList<ProgramChain> ProgramChains { get; init; }

    /// <summary>Gets VTS title/chapter mappings from the VTS_PTT_SRPT table.</summary>
    public required IReadOnlyList<VtsTitlePartMap> TitlePartMaps { get; init; }

}

/// <summary>
/// Maps a title within a VTS to its PTT/chapter entries.
/// </summary>
public record VtsTitlePartMap
{
    /// <summary>Gets the title number within the VTS (VTS_TTN, 1-based).</summary>
    public required int TitleSetTitleNumber { get; init; }

    /// <summary>Gets chapter/part mappings for this title.</summary>
    public required IReadOnlyList<VtsPartOfTitle> Parts { get; init; }
}

/// <summary>
/// Maps one part of title/chapter to a PGC and program number.
/// </summary>
public record VtsPartOfTitle
{
    /// <summary>Gets the part/chapter number within the VTS title (1-based).</summary>
    public required int Number { get; init; }

    /// <summary>Gets the program chain number (PGCN, 1-based).</summary>
    public required int ProgramChainNumber { get; init; }

    /// <summary>Gets the program number within the program chain (PGN, 1-based).</summary>
    public required int ProgramNumber { get; init; }
}

/// <summary>
/// Represents a program chain (title) in a DVD title set.
/// </summary>
public record ProgramChain
{
    /// <summary>Gets the program chain number (1-based PGCN).</summary>
    public required int Number { get; init; }

    /// <summary>Gets the raw entry identifier from the PGCIT search pointer.</summary>
    public required byte EntryId { get; init; }

    /// <summary>Gets whether this PGC is marked as an entry PGC.</summary>
    public required bool IsEntryProgramChain { get; init; }

    /// <summary>Gets the block mode from the PGCIT search pointer.</summary>
    public required DvdCellBlockMode BlockMode { get; init; }

    /// <summary>Gets the block type from the PGCIT search pointer.</summary>
    public required DvdCellBlockType BlockType { get; init; }

    /// <summary>Gets the parental management mask from the PGCIT search pointer.</summary>
    public required ushort ParentalManagementMask { get; init; }

    /// <summary>Gets the number of programs in this program chain.</summary>
    public required int NumberOfPrograms { get; init; }

    /// <summary>Gets the number of cells in this program chain.</summary>
    public required int NumberOfCells { get; init; }

    /// <summary>Gets the total playback time for this program chain.</summary>
    public required DvdPlaybackTime PlaybackTime { get; init; }

    /// <summary>Gets the program numbers that make up this program chain.</summary>
    public required IReadOnlyList<int> ProgramNumbers { get; init; }

    /// <summary>Gets the cell indices referenced by this program chain.</summary>
    public required IReadOnlyList<int> CellIndices { get; init; }

    /// <summary>Gets the PGC program map entries.</summary>
    public required IReadOnlyList<ProgramMapEntry> ProgramMap { get; init; }

    /// <summary>Gets raw per-PGC audio stream controls.</summary>
    public required IReadOnlyList<DvdAudioStreamControl> AudioControls { get; init; }

    /// <summary>Gets raw per-PGC subpicture stream controls.</summary>
    public required IReadOnlyList<DvdSubpictureStreamControl> SubpictureControls { get; init; }

    /// <summary>Gets cell playback records for this PGC.</summary>
    public required IReadOnlyList<CellPlaybackInfo> CellPlayback { get; init; }

    /// <summary>Gets cell position records for this PGC.</summary>
    public required IReadOnlyList<CellPositionInfo> CellPositions { get; init; }

    /// <summary>Gets the next PGC number, or zero when absent.</summary>
    public required int NextProgramChainNumber { get; init; }

    /// <summary>Gets the previous PGC number, or zero when absent.</summary>
    public required int PreviousProgramChainNumber { get; init; }

    /// <summary>Gets the go-up PGC number, or zero when absent.</summary>
    public required int GoUpProgramChainNumber { get; init; }

    /// <summary>Gets whether any cell in this PGC is part of an angle block.</summary>
    public bool HasAngleBlock => CellPlayback.Any(item => item.BlockType == DvdCellBlockType.AngleBlock);

    /// <summary>Gets whether any cell in this PGC uses seamless playback.</summary>
    public bool HasSeamlessPlayback => CellPlayback.Any(item => item.IsSeamlessPlayback);

    /// <summary>Gets whether any logical cell position is referenced more than once.</summary>
    public bool HasRepeatedCells => CellPositions
        .GroupBy(item => new { item.VobId, item.CellId })
        .Any(group => group.Count() > 1);
}

/// <summary>
/// PGC program-map entry, mapping a program to the first cell number it uses.
/// </summary>
public record ProgramMapEntry
{
    /// <summary>Gets the program number (1-based).</summary>
    public required int ProgramNumber { get; init; }

    /// <summary>Gets the first cell number for this program (1-based).</summary>
    public required int FirstCellNumber { get; init; }
}

/// <summary>
/// Audio stream control entry from a PGC.
/// </summary>
public record DvdAudioStreamControl
{
    /// <summary>Gets the VTS audio stream index.</summary>
    public required int StreamIndex { get; init; }

    /// <summary>Gets the raw 16-bit control value.</summary>
    public required ushort RawValue { get; init; }

    /// <summary>Gets whether the control entry marks this stream available for the PGC.</summary>
    public required bool IsAvailable { get; init; }

    /// <summary>Gets the player stream number when present.</summary>
    public int? PlayerStreamNumber { get; init; }
}

/// <summary>
/// Subpicture stream control entry from a PGC.
/// </summary>
public record DvdSubpictureStreamControl
{
    /// <summary>Gets the VTS subpicture stream index.</summary>
    public required int StreamIndex { get; init; }

    /// <summary>Gets the raw 32-bit control value.</summary>
    public required uint RawValue { get; init; }

    /// <summary>Gets whether the control entry marks this stream available for the PGC.</summary>
    public required bool IsAvailable { get; init; }

    /// <summary>Gets the player stream number for 4:3 display when present.</summary>
    public int? FourByThreeStreamNumber { get; init; }

    /// <summary>Gets the player stream number for wide display when present.</summary>
    public int? WideStreamNumber { get; init; }

    /// <summary>Gets the player stream number for letterbox display when present.</summary>
    public int? LetterboxStreamNumber { get; init; }

    /// <summary>Gets the player stream number for pan-scan display when present.</summary>
    public int? PanScanStreamNumber { get; init; }
}

/// <summary>
/// Cell playback block mode.
/// </summary>
public enum DvdCellBlockMode
{
    /// <summary>The cell is not in a block.</summary>
    NotInBlock = 0,

    /// <summary>The cell is the first cell in a block.</summary>
    FirstCell = 1,

    /// <summary>The cell is inside a block.</summary>
    InBlock = 2,

    /// <summary>The cell is the last cell in a block.</summary>
    LastCell = 3
}

/// <summary>
/// Cell playback block type.
/// </summary>
public enum DvdCellBlockType
{
    /// <summary>No special block type.</summary>
    None = 0,

    /// <summary>Angle block.</summary>
    AngleBlock = 1,

    /// <summary>Reserved or unknown block type.</summary>
    Reserved = 2
}

/// <summary>
/// Playback information for a PGC cell.
/// </summary>
public record CellPlaybackInfo
{
    /// <summary>Gets the cell number within the PGC (1-based).</summary>
    public required int Number { get; init; }

    /// <summary>Gets the cell block mode.</summary>
    public required DvdCellBlockMode BlockMode { get; init; }

    /// <summary>Gets the cell block type.</summary>
    public required DvdCellBlockType BlockType { get; init; }

    /// <summary>Gets whether playback from this cell to the next is seamless.</summary>
    public required bool IsSeamlessPlayback { get; init; }

    /// <summary>Gets whether this cell is interleaved.</summary>
    public required bool IsInterleaved { get; init; }

    /// <summary>Gets whether this cell has an STC discontinuity.</summary>
    public required bool HasStcDiscontinuity { get; init; }

    /// <summary>Gets whether this cell participates in seamless angle playback.</summary>
    public required bool IsSeamlessAngle { get; init; }

    /// <summary>Gets whether this cell declares a still time.</summary>
    public required bool HasStillTime { get; init; }

    /// <summary>Gets the still time byte.</summary>
    public required byte StillTime { get; init; }

    /// <summary>Gets the referenced cell command number.</summary>
    public required byte CellCommandNumber { get; init; }

    /// <summary>Gets the cell playback time.</summary>
    public required DvdPlaybackTime PlaybackTime { get; init; }

    /// <summary>Gets the first sector for the cell.</summary>
    public required uint FirstSector { get; init; }

    /// <summary>Gets the first interleaved unit end sector.</summary>
    public required uint FirstInterleavedUnitEndSector { get; init; }

    /// <summary>Gets the last VOBU start sector.</summary>
    public required uint LastVobuStartSector { get; init; }

    /// <summary>Gets the last sector for the cell.</summary>
    public required uint LastSector { get; init; }
}

/// <summary>
/// Position information for a PGC cell.
/// </summary>
public record CellPositionInfo
{
    /// <summary>Gets the cell number within the PGC (1-based).</summary>
    public required int Number { get; init; }

    /// <summary>Gets the VOB identifier number.</summary>
    public required int VobId { get; init; }

    /// <summary>Gets the cell identifier number within the VOB.</summary>
    public required int CellId { get; init; }
}

/// <summary>
/// Represents playback time in DVD time code format.
/// </summary>
public record DvdPlaybackTime
{
    /// <summary>Gets the hours component (0-23).</summary>
    public required int Hours { get; init; }

    /// <summary>Gets the minutes component (0-59).</summary>
    public required int Minutes { get; init; }

    /// <summary>Gets the seconds component (0-59).</summary>
    public required int Seconds { get; init; }

    /// <summary>Gets the frame count component (0-29 for NTSC, 0-24 for PAL).</summary>
    public required int Frames { get; init; }

    /// <summary>Gets whether this time code is in PAL format (true) or NTSC (false).</summary>
    public required bool IsPal { get; init; }

    /// <summary>Returns the total playback time as a TimeSpan.</summary>
    public TimeSpan ToTimeSpan()
    {
        double frameRate = IsPal ? 25.0 : 29.97;
        double frameSeconds = Frames / frameRate;
        return TimeSpan.FromSeconds(Hours * 3600 + Minutes * 60 + Seconds + frameSeconds);
    }

    /// <summary>Returns a string representation of the time code (HH:MM:SS;FF format).</summary>
    public override string ToString() =>
        $"{Hours:D2}:{Minutes:D2}:{Seconds:D2};{Frames:D2}";
}

/// <summary>
/// Represents video stream information from a VTSI file.
/// </summary>
public record VideoStreamInfo
{
    /// <summary>Gets the stream index (0-based).</summary>
    public required int Index { get; init; }

    /// <summary>Gets the video codec used (e.g., "MPEG-2").</summary>
    public required string Codec { get; init; }

    /// <summary>Gets the aspect ratio (e.g., "4:3", "16:9").</summary>
    public required string AspectRatio { get; init; }

    /// <summary>Gets the video resolution (e.g., "720x480" NTSC or "720x576" PAL).</summary>
    public required string Resolution { get; init; }

    /// <summary>Gets whether this is a PAL stream (true) or NTSC (false).</summary>
    public required bool IsPal { get; init; }

    /// <summary>Gets the frame rate in frames per second (typically 29.97 or 25.0).</summary>
    public required double FrameRate { get; init; }

    /// <summary>
    /// Gets whether VTS_V_ATR declares line-21 closed captions in field 1 (CEA-608 CC1/CC2).
    /// This is an authoring declaration from the IFO only; payload user-data is not inspected.
    /// </summary>
    public bool HasLine21ClosedCaptionField1 { get; init; }

    /// <summary>
    /// Gets whether VTS_V_ATR declares line-21 closed captions in field 2 (CEA-608 CC3/CC4).
    /// This is an authoring declaration from the IFO only; payload user-data is not inspected.
    /// </summary>
    public bool HasLine21ClosedCaptionField2 { get; init; }
}

/// <summary>
/// Represents audio stream information from a VTSI file.
/// </summary>
public record AudioStreamInfo
{
    /// <summary>Gets the stream index (0-based).</summary>
    public required int Index { get; init; }

    /// <summary>Gets the audio codec (e.g., "AC-3", "PCM", "MPEG-1 Layer 2").</summary>
    public required string Codec { get; init; }

    /// <summary>Gets the number of channels (e.g., "mono", "stereo", "5.1", "5.1 Surround").</summary>
    public required string Channels { get; init; }

    /// <summary>Gets the sampling frequency in Hz (typically 48000).</summary>
    public required int SamplingFrequency { get; init; }

    /// <summary>Gets the language code (3-character ISO 639-2 code, e.g., "eng" for English).</summary>
    public required string LanguageCode { get; init; }

    /// <summary>Gets whether this audio stream has a content type (e.g., commentary, director's cut).</summary>
    public required bool HasContentType { get; init; }

    /// <summary>Gets the content description if HasContentType is true.</summary>
    public string? ContentType { get; init; }
}

/// <summary>
/// Represents subtitle stream information from a VTSI file.
/// </summary>
public record SubtitleStreamInfo
{
    /// <summary>Gets the stream index (0-based).</summary>
    public required int Index { get; init; }

    /// <summary>Gets the subtitle coding mode (e.g., "Unicode", "8-bit characters").</summary>
    public required string CodingMode { get; init; }

    /// <summary>Gets the language code (3-character ISO 639-2 code, e.g., "eng" for English).</summary>
    public required string LanguageCode { get; init; }

    /// <summary>Gets whether this subtitle stream has a content type (e.g., commentary, karaoke).</summary>
    public required bool HasContentType { get; init; }

    /// <summary>Gets the content description if HasContentType is true.</summary>
    public string? ContentType { get; init; }
}
