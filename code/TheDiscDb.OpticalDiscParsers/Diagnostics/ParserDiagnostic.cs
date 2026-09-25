namespace TheDiscDb.OpticalDiscParsers.Diagnostics;

/// <summary>
/// Severity level for a parser diagnostic.
/// </summary>
public enum DiagnosticSeverity
{
    /// <summary>Informational message; parsing continues normally.</summary>
    Info = 0,

    /// <summary>Warning; parsing continues but with potential data loss or ambiguity.</summary>
    Warning = 1,

    /// <summary>Error; parsing may have stopped or returned partial results.</summary>
    Error = 2,
}

/// <summary>
/// Represents a single diagnostic message from parsing.
/// Includes file offset and structured context to aid debugging and validation.
/// </summary>
/// <param name="Offset">Byte offset in the source file where the issue occurred.</param>
/// <param name="Severity">Severity level of the diagnostic.</param>
/// <param name="Code">Machine-readable diagnostic code (e.g., "DVD_INVALID_OFFSET", "BR_TRUNCATED_TABLE").</param>
/// <param name="Message">Human-readable diagnostic message.</param>
public record ParserDiagnostic(
    long Offset,
    DiagnosticSeverity Severity,
    string Code,
    string Message);
