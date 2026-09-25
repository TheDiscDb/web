namespace TheDiscDb.OpticalDiscParsers.Input;

using Diagnostics;

/// <summary>
/// Concrete implementation of <see cref="BinaryReaderBase"/> using an <see cref="IOpticalDiscReader"/>.
/// Provides async binary reading with bounds checking and diagnostic accumulation.
/// </summary>
internal sealed class OpticalDiscBinaryReader : BinaryReaderBase
{
    private readonly IOpticalDiscReader reader;

    /// <summary>
    /// Creates a new optical disc binary reader.
    /// </summary>
    public OpticalDiscBinaryReader(IOpticalDiscReader reader, DiagnosticBag diagnostics)
        : base(diagnostics)
    {
        this.reader = reader ?? throw new ArgumentNullException(nameof(reader));
    }

    /// <inheritdoc />
    protected override async ValueTask<ReadOnlyMemory<byte>> ReadCoreAsync(long offset, int length)
    {
        return await reader.ReadAsync(offset, length);
    }
}
