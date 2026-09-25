namespace TheDiscDb.OpticalDiscParsers.Diagnostics;

/// <summary>
/// Accumulator for parser diagnostics.
/// Diagnostics are collected during parsing without throwing exceptions,
/// allowing parsers to return partial results for malformed input.
/// </summary>
public sealed class DiagnosticBag
{
    private readonly List<ParserDiagnostic> diagnostics = new();

    /// <summary>
    /// Gets an immutable list of all accumulated diagnostics.
    /// </summary>
    public IReadOnlyList<ParserDiagnostic> Diagnostics => diagnostics.AsReadOnly();

    /// <summary>
    /// Indicates whether any errors have been recorded.
    /// </summary>
    public bool HasErrors => diagnostics.Any(d => d.Severity == DiagnosticSeverity.Error);

    /// <summary>
    /// Indicates whether any warnings or errors have been recorded.
    /// </summary>
    public bool HasWarnings => diagnostics.Any(d => d.Severity >= DiagnosticSeverity.Warning);

    /// <summary>
    /// Records an informational diagnostic.
    /// </summary>
    public void Info(long offset, string code, string message)
    {
        diagnostics.Add(new ParserDiagnostic(offset, DiagnosticSeverity.Info, code, message));
    }

    /// <summary>
    /// Records a warning diagnostic.
    /// </summary>
    public void Warning(long offset, string code, string message)
    {
        diagnostics.Add(new ParserDiagnostic(offset, DiagnosticSeverity.Warning, code, message));
    }

    /// <summary>
    /// Records an error diagnostic.
    /// </summary>
    public void Error(long offset, string code, string message)
    {
        diagnostics.Add(new ParserDiagnostic(offset, DiagnosticSeverity.Error, code, message));
    }

    /// <summary>
    /// Clears all accumulated diagnostics.
    /// </summary>
    public void Clear()
    {
        diagnostics.Clear();
    }
}
