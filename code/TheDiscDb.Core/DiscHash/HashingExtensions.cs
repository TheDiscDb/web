using System.Buffers.Binary;

namespace TheDiscDb.Core.DiscHash;

public static class HashingExtensions
{
    /// <summary>
    /// Computes MD5 over file sizes only, each encoded as eight little-endian bytes
    /// in enumeration order. Returns 32 uppercase hexadecimal characters without separators.
    /// Callers supply the canonical file order; names, indices, and timestamps are not hashed.
    /// </summary>
    public static string CalculateHash(this IEnumerable<FileHashInfo> files)
    {
        var hash = new Md5Digest();
        Span<byte> fileSizeBytes = stackalloc byte[sizeof(long)];

        foreach (var file in files)
        {
            BinaryPrimitives.WriteInt64LittleEndian(fileSizeBytes, file.Size);
            hash.Append(fileSizeBytes);
        }

        return Convert.ToHexString(hash.Finish());
    }
}