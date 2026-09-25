namespace TheDiscDb.OpticalDiscParsers.Models;

using Diagnostics;

/// <summary>
/// Represents the outcome of a parser operation.
/// Contains either a parsed result, diagnostics, or both.
/// </summary>
/// <typeparam name="T">Type of the parsed result.</typeparam>
/// <param name="Value">The parsed result, or null if parsing failed or was incomplete.</param>
/// <param name="Diagnostics">Diagnostics accumulated during parsing.</param>
public record ParserResult<T>(T? Value, IReadOnlyList<ParserDiagnostic> Diagnostics) where T : class
{
    /// <summary>
    /// Indicates whether parsing succeeded (result is non-null and no errors occurred).
    /// </summary>
    public bool IsSuccessful => Value != null && !Diagnostics.Any(d => d.Severity == DiagnosticSeverity.Error);

    /// <summary>
    /// Indicates whether the result is partial (non-null but with errors or warnings).
    /// </summary>
    public bool IsPartial => Value != null && (Diagnostics.Any(d => d.Severity >= DiagnosticSeverity.Warning));

    /// <summary>
    /// Indicates whether parsing completely failed (no result and errors present).
    /// </summary>
    public bool IsFailed => Value == null && Diagnostics.Any(d => d.Severity == DiagnosticSeverity.Error);

    /// <summary>
    /// Gets the first error diagnostic, if any.
    /// </summary>
    public ParserDiagnostic? FirstError =>
        Diagnostics.FirstOrDefault(d => d.Severity == DiagnosticSeverity.Error);
}
