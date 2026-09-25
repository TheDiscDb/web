namespace TheDiscDb.OpticalDiscParsers.Input;

/// <summary>
/// Concrete implementation of <see cref="BinaryReaderBase"/> using an <see cref="IOpticalDiscReader"/>.
/// Provides async binary reading with bounds checking and diagnostic accumulation.
/// </summary>
public sealed class OpticalDiscBinaryReader : BinaryReaderBase
{
    private readonly IOpticalDiscReader reader;

    /// <summary>
    /// Creates a new optical disc binary reader.
    /// </summary>
    public OpticalDiscBinaryReader(IOpticalDiscReader reader)
    {
        this.reader = reader ?? throw new ArgumentNullException(nameof(reader));
    }

    /// <inheritdoc />
    protected override async ValueTask<ReadOnlyMemory<byte>> ReadCoreAsync(long offset, int length)
    {
        return await reader.ReadAsync(offset, length);
    }
}
