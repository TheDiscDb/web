namespace TheDiscDb.OpticalDiscParsers.Input;

using System.Buffers.Binary;
using Diagnostics;

/// <summary>
/// Base class for defensive, bounds-checking binary readers.
/// Validates every read operation and emits diagnostics for malformed data.
/// </summary>
public abstract class BinaryReaderBase
{
    private long position;

    /// <summary>
    /// Creates a new binary reader with initial position 0.
    /// </summary>
    protected BinaryReaderBase()
    {
        position = 0;
    }

    /// <summary>
    /// Gets the current read position.
    /// </summary>
    public long Position => position;

    /// <summary>
    /// Gets the diagnostic bag for this reader.
    /// </summary>
    public DiagnosticBag Diagnostics { get; protected init; } = new();

    /// <summary>
    /// Reads bytes from the source asynchronously.
    /// </summary>
    /// <param name="offset">Byte offset to read from.</param>
    /// <param name="length">Number of bytes to read.</param>
    /// <returns>Read data or empty if read fails.</returns>
    protected abstract ValueTask<ReadOnlyMemory<byte>> ReadCoreAsync(long offset, int length);

    /// <summary>
    /// Reads a big-endian UInt32 at the current position and advances.
    /// Emits a diagnostic and returns 0 if read fails.
    /// </summary>
    public async ValueTask<uint> ReadUInt32BigEndianAsync()
    {
        const int size = sizeof(uint);
        if (!TryCheckBounds(position, size, out var error))
        {
            Diagnostics.Error(position, "BOUNDS_ERROR", error);
            return 0;
        }

        var data = await ReadCoreAsync(position, size);
        if (data.Length < size)
        {
            Diagnostics.Error(position, "TRUNCATED_READ", $"Expected {size} bytes, got {data.Length}");
            return 0;
        }

        var value = BinaryPrimitives.ReadUInt32BigEndian(data.Span);
        position += size;
        return value;
    }

    /// <summary>
    /// Reads a little-endian UInt32 at the current position and advances.
    /// Emits a diagnostic and returns 0 if read fails.
    /// </summary>
    public async ValueTask<uint> ReadUInt32LittleEndianAsync()
    {
        const int size = sizeof(uint);
        if (!TryCheckBounds(position, size, out var error))
        {
            Diagnostics.Error(position, "BOUNDS_ERROR", error);
            return 0;
        }

        var data = await ReadCoreAsync(position, size);
        if (data.Length < size)
        {
            Diagnostics.Error(position, "TRUNCATED_READ", $"Expected {size} bytes, got {data.Length}");
            return 0;
        }

        var value = BinaryPrimitives.ReadUInt32LittleEndian(data.Span);
        position += size;
        return value;
    }

    /// <summary>
    /// Reads a big-endian UInt16 at the current position and advances.
    /// </summary>
    public async ValueTask<ushort> ReadUInt16BigEndianAsync()
    {
        const int size = sizeof(ushort);
        if (!TryCheckBounds(position, size, out var error))
        {
            Diagnostics.Error(position, "BOUNDS_ERROR", error);
            return 0;
        }

        var data = await ReadCoreAsync(position, size);
        if (data.Length < size)
        {
            Diagnostics.Error(position, "TRUNCATED_READ", $"Expected {size} bytes, got {data.Length}");
            return 0;
        }

        var value = BinaryPrimitives.ReadUInt16BigEndian(data.Span);
        position += size;
        return value;
    }

    /// <summary>
    /// Reads a byte at the current position and advances.
    /// </summary>
    public async ValueTask<byte> ReadByteAsync()
    {
        const int size = sizeof(byte);
        if (!TryCheckBounds(position, size, out var error))
        {
            Diagnostics.Error(position, "BOUNDS_ERROR", error);
            return 0;
        }

        var data = await ReadCoreAsync(position, size);
        if (data.Length < size)
        {
            Diagnostics.Error(position, "TRUNCATED_READ", "Expected 1 byte, got 0");
            return 0;
        }

        var value = data.Span[0];
        position += size;
        return value;
    }

    /// <summary>
    /// Reads bytes at the current position and advances.
    /// </summary>
    /// <param name="length">Number of bytes to read.</param>
    /// <returns>Read data or empty if read fails.</returns>
    public async ValueTask<ReadOnlyMemory<byte>> ReadBytesAsync(int length)
    {
        if (length < 0)
        {
            Diagnostics.Error(position, "INVALID_LENGTH", $"Requested length {length} is negative");
            return ReadOnlyMemory<byte>.Empty;
        }

        if (length == 0)
        {
            return ReadOnlyMemory<byte>.Empty;
        }

        if (!TryCheckBounds(position, length, out var error))
        {
            Diagnostics.Error(position, "BOUNDS_ERROR", error);
            return ReadOnlyMemory<byte>.Empty;
        }

        var data = await ReadCoreAsync(position, length);
        if (data.Length < length)
        {
            Diagnostics.Warning(position, "PARTIAL_READ", $"Expected {length} bytes, got {data.Length}");
        }

        position += data.Length;
        return data;
    }

    /// <summary>
    /// Seeks to an absolute offset.
    /// </summary>
    public void Seek(long offset)
    {
        if (offset < 0)
        {
            Diagnostics.Error(offset, "INVALID_SEEK", "Seek offset cannot be negative");
            return;
        }

        position = offset;
    }

    /// <summary>
    /// Validates an offset and length combination for bounds.
    /// </summary>
    private static bool TryCheckBounds(long offset, int length, out string error)
    {
        error = string.Empty;

        if (offset < 0)
        {
            error = "Offset is negative";
            return false;
        }

        if (length < 0)
        {
            error = "Length is negative";
            return false;
        }

        // Check for overflow
        if (length > int.MaxValue - offset || offset + length < 0)
        {
            error = "Offset + length overflow";
            return false;
        }

        return true;
    }
}
