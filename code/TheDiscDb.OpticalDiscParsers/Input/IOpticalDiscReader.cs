namespace TheDiscDb.OpticalDiscParsers.Input;

/// <summary>
/// Represents a random-access, bounded reader for optical disc binary data.
/// Supports both full in-memory reads (.NET) and bounded Blob-based reads (Blazor WebAssembly).
/// </summary>
public interface IOpticalDiscReader
{
    /// <summary>
    /// Reads data asynchronously from the specified offset.
    /// </summary>
    /// <param name="offset">Byte offset to read from (0-based).</param>
    /// <param name="length">Number of bytes to read.</param>
    /// <returns>A read-only memory containing the requested bytes, or empty if read fails.</returns>
    ValueTask<ReadOnlyMemory<byte>> ReadAsync(long offset, int length);

    /// <summary>
    /// Gets the total size of the data source asynchronously.
    /// </summary>
    /// <returns>Total size in bytes.</returns>
    ValueTask<long> GetLengthAsync();
}
