namespace TheDiscDb.Core.DiscHash;

/// <summary>
/// Computes the DVD Disc ID exactly as libdvdread's <c>DVDDiscID()</c> does (the value
/// surfaced by tools such as <c>lsdvd</c>): <c>uppercase hex( MD5( concatenated IFO files ) )</c>.
/// <para>
/// The IFO files are hashed in order: <c>VIDEO_TS.IFO</c> first, then
/// <c>VTS_01_0.IFO</c>, <c>VTS_02_0.IFO</c>, … The number of title sets is read from
/// <c>VIDEO_TS.IFO</c> (the VMGI_MAT <c>vmg_nr_of_title_sets</c> field at offset
/// <c>0x3E</c>, big-endian). libdvdread hashes <c>VIDEO_TS.IFO</c> plus that many VTS
/// IFOs, capped at <b>10 files total</b>, skipping any that are missing.
/// </para>
/// <para>
/// DVD-Video IFO files are always a whole number of 2048-byte logical blocks, so hashing
/// the raw file bytes matches libdvdread's block-aligned read. This is the DVD counterpart
/// of the AACS <see cref="AacsDiscId"/> used for Blu-ray/UHD.
/// </para>
/// </summary>
public static class DvdDiscId
{
    /// <summary>Maximum number of IFO files libdvdread includes (VIDEO_TS.IFO + up to 9 VTS IFOs).</summary>
    public const int MaxFiles = 10;

    /// <summary>Offset of the big-endian <c>vmg_nr_of_title_sets</c> field in <c>VIDEO_TS.IFO</c>.</summary>
    private const int TitleSetCountOffset = 0x3E;

    /// <summary>
    /// Reads <c>vmg_nr_of_title_sets</c> (number of Video Title Sets) from the
    /// <c>VIDEO_TS.IFO</c> bytes — a 2-byte big-endian value at offset <c>0x3E</c>.
    /// </summary>
    public static int ReadTitleSetCount(byte[] videoTsIfo)
    {
        ArgumentNullException.ThrowIfNull(videoTsIfo);
        if (videoTsIfo.Length < TitleSetCountOffset + 2)
        {
            throw new ArgumentException(
                "VIDEO_TS.IFO is too small to contain the title-set count.", nameof(videoTsIfo));
        }

        return (videoTsIfo[TitleSetCountOffset] << 8) | videoTsIfo[TitleSetCountOffset + 1];
    }

    /// <summary>
    /// Computes the DVD Disc ID from the raw IFO bytes.
    /// </summary>
    /// <param name="videoTsIfo">Raw bytes of <c>VIDEO_TS.IFO</c>.</param>
    /// <param name="vtsIfos">
    /// Raw bytes of the per-title-set IFOs in order, where <paramref name="vtsIfos"/>[0] is
    /// <c>VTS_01_0.IFO</c>, [1] is <c>VTS_02_0.IFO</c>, and so on. Entries may be <c>null</c>
    /// for a title set whose IFO is missing (it is skipped, matching libdvdread).
    /// </param>
    /// <returns>A 32-character uppercase hex MD5 string with no separators.</returns>
    public static string Compute(byte[] videoTsIfo, IReadOnlyList<byte[]?> vtsIfos)
    {
        ArgumentNullException.ThrowIfNull(videoTsIfo);
        ArgumentNullException.ThrowIfNull(vtsIfos);

        // libdvdread: title_sets = vmg_nr_of_title_sets + 1 (the +1 counts VIDEO_TS.IFO itself),
        // then capped at 10. The loop runs title = 0 .. title_sets-1, where title 0 is
        // VIDEO_TS.IFO and title N is VTS_0N_0.IFO.
        int titleSets = ReadTitleSetCount(videoTsIfo) + 1;
        if (titleSets > MaxFiles)
        {
            titleSets = MaxFiles;
        }

        // Managed MD5 (Md5Digest) is used instead of System.Security.Cryptography.MD5
        // because the latter throws Cryptography_UnknownHashAlgorithm in Blazor WASM
        // (the WASM crypto backend only exposes the SHA family via SubtleCrypto).
        var md5 = new Md5Digest();
        md5.Append(videoTsIfo);

        for (int title = 1; title < titleSets; title++)
        {
            int index = title - 1;
            if (index < vtsIfos.Count && vtsIfos[index] is { } ifo)
            {
                md5.Append(ifo);
            }
        }

        return Convert.ToHexString(md5.Finish());
    }
}
