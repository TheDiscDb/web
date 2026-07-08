namespace TheDiscDb.Core.DiscHash;

/// <summary>
/// Pure managed MD5 (RFC 1321). Used because <see cref="System.Security.Cryptography.MD5"/>
/// throws <c>Cryptography_UnknownHashAlgorithm</c> in Blazor WebAssembly (the WASM crypto
/// backend only exposes the SHA family via the browser's SubtleCrypto, which has no MD5).
/// This implementation runs identically on the server and in WASM.
/// <para>Supports incremental hashing so large concatenations (e.g. DVD IFO files) don't
/// need to be materialised into a single buffer.</para>
/// </summary>
public sealed class Md5Digest
{
    private static readonly uint[] K =
    [
        0xd76aa478, 0xe8c7b756, 0x242070db, 0xc1bdceee,
        0xf57c0faf, 0x4787c62a, 0xa8304613, 0xfd469501,
        0x698098d8, 0x8b44f7af, 0xffff5bb1, 0x895cd7be,
        0x6b901122, 0xfd987193, 0xa679438e, 0x49b40821,
        0xf61e2562, 0xc040b340, 0x265e5a51, 0xe9b6c7aa,
        0xd62f105d, 0x02441453, 0xd8a1e681, 0xe7d3fbc8,
        0x21e1cde6, 0xc33707d6, 0xf4d50d87, 0x455a14ed,
        0xa9e3e905, 0xfcefa3f8, 0x676f02d9, 0x8d2a4c8a,
        0xfffa3942, 0x8771f681, 0x6d9d6122, 0xfde5380c,
        0xa4beea44, 0x4bdecfa9, 0xf6bb4b60, 0xbebfbc70,
        0x289b7ec6, 0xeaa127fa, 0xd4ef3085, 0x04881d05,
        0xd9d4d039, 0xe6db99e5, 0x1fa27cf8, 0xc4ac5665,
        0xf4292244, 0x432aff97, 0xab9423a7, 0xfc93a039,
        0x655b59c3, 0x8f0ccc92, 0xffeff47d, 0x85845dd1,
        0x6fa87e4f, 0xfe2ce6e0, 0xa3014314, 0x4e0811a1,
        0xf7537e82, 0xbd3af235, 0x2ad7d2bb, 0xeb86d391,
    ];

    private static readonly int[] S =
    [
        7, 12, 17, 22, 7, 12, 17, 22, 7, 12, 17, 22, 7, 12, 17, 22,
        5, 9, 14, 20, 5, 9, 14, 20, 5, 9, 14, 20, 5, 9, 14, 20,
        4, 11, 16, 23, 4, 11, 16, 23, 4, 11, 16, 23, 4, 11, 16, 23,
        6, 10, 15, 21, 6, 10, 15, 21, 6, 10, 15, 21, 6, 10, 15, 21,
    ];

    private uint a0 = 0x67452301, b0 = 0xefcdab89, c0 = 0x98badcfe, d0 = 0x10325476;
    private readonly byte[] block = new byte[64];
    private int blockLen;
    private ulong totalBytes;
    private bool finished;

    /// <summary>Feeds more bytes into the running digest.</summary>
    public void Append(ReadOnlySpan<byte> data)
    {
        if (finished)
        {
            throw new InvalidOperationException("Digest already finalized.");
        }

        totalBytes += (ulong)data.Length;
        int offset = 0;

        if (blockLen > 0)
        {
            int need = 64 - blockLen;
            int take = Math.Min(need, data.Length);
            data.Slice(0, take).CopyTo(block.AsSpan(blockLen));
            blockLen += take;
            offset += take;
            if (blockLen == 64)
            {
                ProcessBlock(block);
                blockLen = 0;
            }
        }

        while (data.Length - offset >= 64)
        {
            ProcessBlock(data.Slice(offset, 64));
            offset += 64;
        }

        int remaining = data.Length - offset;
        if (remaining > 0)
        {
            data.Slice(offset, remaining).CopyTo(block);
            blockLen = remaining;
        }
    }

    /// <summary>Finalizes the digest and returns the 16-byte MD5 hash.</summary>
    public byte[] Finish()
    {
        if (finished)
        {
            throw new InvalidOperationException("Digest already finalized.");
        }

        ulong bitLength = totalBytes * 8;

        // Append the 0x80 padding byte, then zeros, leaving room for the 8-byte length.
        block[blockLen++] = 0x80;
        if (blockLen > 56)
        {
            while (blockLen < 64)
            {
                block[blockLen++] = 0;
            }

            ProcessBlock(block);
            blockLen = 0;
        }

        while (blockLen < 56)
        {
            block[blockLen++] = 0;
        }

        for (int i = 0; i < 8; i++)
        {
            block[56 + i] = (byte)(bitLength >> (8 * i));
        }

        ProcessBlock(block);

        var result = new byte[16];
        WriteLittleEndian(result, 0, a0);
        WriteLittleEndian(result, 4, b0);
        WriteLittleEndian(result, 8, c0);
        WriteLittleEndian(result, 12, d0);
        finished = true;
        return result;
    }

    /// <summary>One-shot MD5 over a single buffer.</summary>
    public static byte[] HashData(ReadOnlySpan<byte> data)
    {
        var md5 = new Md5Digest();
        md5.Append(data);
        return md5.Finish();
    }

    private void ProcessBlock(ReadOnlySpan<byte> buf)
    {
        Span<uint> m = stackalloc uint[16];
        for (int i = 0; i < 16; i++)
        {
            int j = i * 4;
            m[i] = (uint)(buf[j] | (buf[j + 1] << 8) | (buf[j + 2] << 16) | (buf[j + 3] << 24));
        }

        uint a = a0, b = b0, c = c0, d = d0;
        for (int i = 0; i < 64; i++)
        {
            uint f;
            int g;
            if (i < 16)
            {
                f = (b & c) | (~b & d);
                g = i;
            }
            else if (i < 32)
            {
                f = (d & b) | (~d & c);
                g = ((5 * i) + 1) & 15;
            }
            else if (i < 48)
            {
                f = b ^ c ^ d;
                g = ((3 * i) + 5) & 15;
            }
            else
            {
                f = c ^ (b | ~d);
                g = (7 * i) & 15;
            }

            f = f + a + K[i] + m[g];
            a = d;
            d = c;
            c = b;
            b += RotateLeft(f, S[i]);
        }

        a0 += a;
        b0 += b;
        c0 += c;
        d0 += d;
    }

    private static uint RotateLeft(uint value, int count) => (value << count) | (value >> (32 - count));

    private static void WriteLittleEndian(byte[] buffer, int offset, uint value)
    {
        buffer[offset] = (byte)value;
        buffer[offset + 1] = (byte)(value >> 8);
        buffer[offset + 2] = (byte)(value >> 16);
        buffer[offset + 3] = (byte)(value >> 24);
    }
}
