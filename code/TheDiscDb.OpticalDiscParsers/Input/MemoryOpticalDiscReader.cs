namespace TheDiscDb.OpticalDiscParsers.Input;

/// <summary>
/// In-memory implementation of <see cref="IOpticalDiscReader"/>.
/// Used for .NET testing and scenarios where the entire binary is loaded into memory.
/// </summary>
public sealed class MemoryOpticalDiscReader : IOpticalDiscReader
{
    private readonly ReadOnlyMemory<byte> data;

    /// <summary>
    /// Creates a new in-memory reader from binary data.
    /// </summary>
    /// <param name="data">Binary data to read from.</param>
    public MemoryOpticalDiscReader(ReadOnlyMemory<byte> data)
    {
        this.data = data;
    }

    /// <summary>
    /// Creates a new in-memory reader from a byte array.
    /// </summary>
    /// <param name="data">Binary data to read from.</param>
    public MemoryOpticalDiscReader(byte[] data) : this(new ReadOnlyMemory<byte>(data))
    {
    }

    /// <inheritdoc />
    public ValueTask<ReadOnlyMemory<byte>> ReadAsync(long offset, int length)
    {
        if (offset < 0 || offset > data.Length)
        {
            return new ValueTask<ReadOnlyMemory<byte>>(ReadOnlyMemory<byte>.Empty);
        }

        var availableLength = (int)Math.Min(length, data.Length - offset);
        if (availableLength <= 0)
        {
            return new ValueTask<ReadOnlyMemory<byte>>(ReadOnlyMemory<byte>.Empty);
        }

        var result = data.Slice((int)offset, availableLength);
        return new ValueTask<ReadOnlyMemory<byte>>(result);
    }

    /// <inheritdoc />
    public ValueTask<long> GetLengthAsync()
    {
        return new ValueTask<long>(data.Length);
    }
}
