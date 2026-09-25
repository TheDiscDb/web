namespace TheDiscDb.OpticalDiscParsers.Bdmv.Models;

using TheDiscDb.OpticalDiscParsers.Diagnostics;

/// <summary>
/// Represents parsed information from a Blu-ray Disc Movie index.bdmv file.
/// </summary>
public record BdmvIndex
{
    /// <summary>Gets the file identifier string, always "INDX" for index.bdmv.</summary>
    public required string Identifier { get; init; }

    /// <summary>Gets the BDMV format version string, such as "0200" or "0300".</summary>
    public required string Version { get; init; }

    /// <summary>Gets the byte offset where the index table starts.</summary>
    public required uint IndexStartAddress { get; init; }

    /// <summary>Gets the byte offset where extension data starts, or 0 when absent.</summary>
    public required uint ExtensionDataStartAddress { get; init; }

    /// <summary>Gets the AppInfoBDMV block.</summary>
    public required BdmvAppInfo AppInfo { get; init; }

    /// <summary>Gets the first-play navigation object.</summary>
    public required BdmvNavigationObject FirstPlayback { get; init; }

    /// <summary>Gets the top-menu navigation object.</summary>
    public required BdmvNavigationObject TopMenu { get; init; }

    /// <summary>Gets the title entries from the index table.</summary>
    public required IReadOnlyList<BdmvTitle> Titles { get; init; }

    /// <summary>Gets diagnostics generated while parsing.</summary>
    public required IReadOnlyList<ParserDiagnostic> Diagnostics { get; init; }
}

/// <summary>
/// Represents the AppInfoBDMV block from index.bdmv.
/// </summary>
public record BdmvAppInfo
{
    /// <summary>Gets the declared AppInfoBDMV payload length.</summary>
    public required uint Length { get; init; }

    /// <summary>Gets whether the disc prefers initial output mode.</summary>
    public required bool InitialOutputModePreference { get; init; }

    /// <summary>Gets whether content-exist flag is set.</summary>
    public required bool ContentExists { get; init; }

    /// <summary>Gets the raw video format code from AppInfoBDMV.</summary>
    public required int VideoFormatCode { get; init; }

    /// <summary>Gets the raw frame-rate code from AppInfoBDMV.</summary>
    public required int FrameRateCode { get; init; }

    /// <summary>Gets the user data decoded as ASCII, trimmed of null padding.</summary>
    public required string UserData { get; init; }
}

/// <summary>
/// Represents a first-play, top-menu, or title navigation object reference.
/// </summary>
public record BdmvNavigationObject
{
    /// <summary>Gets the raw object type code.</summary>
    public required int ObjectTypeCode { get; init; }

    /// <summary>Gets the object type name, typically "HDMV" or "BD-J".</summary>
    public required string ObjectType { get; init; }

    /// <summary>Gets the raw playback type code.</summary>
    public required int PlaybackTypeCode { get; init; }

    /// <summary>Gets the playback type name when known.</summary>
    public required string PlaybackType { get; init; }

    /// <summary>Gets the HDMV MovieObject identifier reference, when this is an HDMV object.</summary>
    public int? IdReference { get; init; }

    /// <summary>Gets the BD-J application name, when this is a BD-J object.</summary>
    public string? Name { get; init; }
}

/// <summary>
/// Represents a normal title entry in index.bdmv.
/// </summary>
public record BdmvTitle
{
    /// <summary>Gets the title index from the title table.</summary>
    public required int Index { get; init; }

    /// <summary>Gets the raw title access type code.</summary>
    public required int AccessTypeCode { get; init; }

    /// <summary>Gets the title access type name when known.</summary>
    public required string AccessType { get; init; }

    /// <summary>Gets the navigation object referenced by this title.</summary>
    public required BdmvNavigationObject Object { get; init; }
}

/// <summary>
/// Represents parsed information from a Blu-ray MovieObject.bdmv file.
/// </summary>
public record BdmvMovieObjects
{
    /// <summary>Gets the file identifier string, always "MOBJ" for MovieObject.bdmv.</summary>
    public required string Identifier { get; init; }

    /// <summary>Gets the BDMV format version string, such as "0200" or "0300".</summary>
    public required string Version { get; init; }

    /// <summary>Gets the byte offset where extension data starts, or 0 when absent.</summary>
    public required uint ExtensionDataStartAddress { get; init; }

    /// <summary>Gets the declared MovieObject payload length.</summary>
    public required uint DataLength { get; init; }

    /// <summary>Gets the movie object entries.</summary>
    public required IReadOnlyList<BdmvMovieObject> Objects { get; init; }

    /// <summary>Gets diagnostics generated while parsing.</summary>
    public required IReadOnlyList<ParserDiagnostic> Diagnostics { get; init; }
}

/// <summary>
/// Represents one HDMV movie object and its navigation commands.
/// </summary>
public record BdmvMovieObject
{
    /// <summary>Gets the object index in MovieObject.bdmv.</summary>
    public required int Index { get; init; }

    /// <summary>Gets whether resume is intended for this object.</summary>
    public required bool ResumeIntentionFlag { get; init; }

    /// <summary>Gets whether menu-call is masked for this object.</summary>
    public required bool MenuCallMask { get; init; }

    /// <summary>Gets whether title-search is masked for this object.</summary>
    public required bool TitleSearchMask { get; init; }

    /// <summary>Gets the number of navigation commands declared for this object.</summary>
    public required int NumberOfCommands { get; init; }

    /// <summary>Gets parsed navigation commands.</summary>
    public required IReadOnlyList<BdmvNavigationCommand> Commands { get; init; }
}

/// <summary>
/// Represents one 12-byte HDMV navigation command.
/// </summary>
public record BdmvNavigationCommand
{
    /// <summary>Gets the command index within the movie object.</summary>
    public required int Index { get; init; }

    /// <summary>Gets the operation count field.</summary>
    public required int OperationCount { get; init; }

    /// <summary>Gets the instruction group field.</summary>
    public required int Group { get; init; }

    /// <summary>Gets the instruction sub-group field.</summary>
    public required int SubGroup { get; init; }

    /// <summary>Gets whether operand 1 is immediate.</summary>
    public required bool ImmediateOperand1 { get; init; }

    /// <summary>Gets whether operand 2 is immediate.</summary>
    public required bool ImmediateOperand2 { get; init; }

    /// <summary>Gets the branch option field.</summary>
    public required int BranchOption { get; init; }

    /// <summary>Gets the compare option field.</summary>
    public required int CompareOption { get; init; }

    /// <summary>Gets the set option field.</summary>
    public required int SetOption { get; init; }

    /// <summary>Gets the 32-bit destination operand.</summary>
    public required uint Destination { get; init; }

    /// <summary>Gets the 32-bit source operand.</summary>
    public required uint Source { get; init; }
}
