namespace TheDiscDb.OpticalDiscParsers.Diagnostics;

/// <summary>
/// Accumulator for parser diagnostics.
/// Diagnostics are collected during parsing without throwing exceptions,
/// allowing parsers to return partial results for malformed input.
/// </summary>
internal sealed class DiagnosticBag
{
    private readonly List<ParserDiagnostic> diagnostics = new();

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
    /// Creates an immutable snapshot of all accumulated diagnostics.
    /// </summary>
    public IReadOnlyList<ParserDiagnostic> Snapshot() =>
        Array.AsReadOnly(diagnostics.ToArray());
}
