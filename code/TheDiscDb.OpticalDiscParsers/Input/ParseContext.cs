namespace TheDiscDb.OpticalDiscParsers.Input;

using Diagnostics;

internal sealed class ParseContext
{
    public ParseContext(IOpticalDiscReader source)
    {
        Diagnostics = new DiagnosticBag();
        Reader = new OpticalDiscBinaryReader(source, Diagnostics);
    }

    public OpticalDiscBinaryReader Reader { get; }

    public DiagnosticBag Diagnostics { get; }
}
