namespace TheDiscDb.UnitTests.Core.DiscHash;

using System.Security.Cryptography;
using TheDiscDb.Core.DiscHash;

public class DvdDiscIdTests
{
    // Builds a minimal VIDEO_TS.IFO whose vmg_nr_of_title_sets (big-endian u16 @ 0x3E) is set.
    private static byte[] MakeVideoTsIfo(int titleSetCount, int size = 0x100, byte fill = 0xAB)
    {
        var bytes = new byte[size];
        for (int i = 0; i < size; i++)
        {
            bytes[i] = fill;
        }

        bytes[0x3E] = (byte)((titleSetCount >> 8) & 0xFF);
        bytes[0x3F] = (byte)(titleSetCount & 0xFF);
        return bytes;
    }

    private static string ExpectedMd5(params byte[][] parts)
    {
        using var ms = new MemoryStream();
        foreach (var p in parts)
        {
            ms.Write(p, 0, p.Length);
        }

        return Convert.ToHexString(MD5.HashData(ms.ToArray()));
    }

    [Test]
    public async Task ReadTitleSetCount_ParsesBigEndianU16()
    {
        var ifo = MakeVideoTsIfo(0x0102); // 258

        await Assert.That(DvdDiscId.ReadTitleSetCount(ifo)).IsEqualTo(0x0102);
    }

    [Test]
    public async Task ReadTitleSetCount_TooSmall_Throws()
    {
        await Assert.That(() => DvdDiscId.ReadTitleSetCount(new byte[8]))
            .Throws<ArgumentException>();
    }

    [Test]
    public async Task Compute_ZeroTitleSets_HashesOnlyVideoTsIfo()
    {
        var videoTs = MakeVideoTsIfo(0);

        var id = DvdDiscId.Compute(videoTs, Array.Empty<byte[]?>());

        await Assert.That(id).IsEqualTo(ExpectedMd5(videoTs));
    }

    [Test]
    public async Task Compute_OneTitleSet_HashesVideoTsThenVts01_InOrder()
    {
        var videoTs = MakeVideoTsIfo(1);
        var vts01 = new byte[] { 1, 2, 3, 4, 5, 6, 7, 8 };

        var id = DvdDiscId.Compute(videoTs, new byte[]?[] { vts01 });

        await Assert.That(id).IsEqualTo(ExpectedMd5(videoTs, vts01));
    }

    [Test]
    public async Task Compute_CapsAtTenFilesTotal()
    {
        // 20 title sets requested, but libdvdread caps the loop at 10 files total:
        // VIDEO_TS.IFO + the first 9 VTS IFOs.
        var videoTs = MakeVideoTsIfo(20);
        var vts = Enumerable.Range(0, 12)
            .Select(i => new byte[] { (byte)i, (byte)(i + 100) })
            .ToArray();

        var id = DvdDiscId.Compute(videoTs, vts);

        var expectedParts = new List<byte[]> { videoTs };
        expectedParts.AddRange(vts.Take(9)); // only first 9 VTS IFOs
        var expected = ExpectedMd5(expectedParts.ToArray());

        await Assert.That(id).IsEqualTo(expected);
    }

    [Test]
    public async Task Compute_SkipsMissingIfos()
    {
        var videoTs = MakeVideoTsIfo(3); // VIDEO_TS + VTS01..VTS03
        var vts01 = new byte[] { 10, 11 };
        var vts03 = new byte[] { 30, 31 };

        // VTS02 missing (null) -> skipped, like libdvdread when DVDOpenFile returns NULL.
        var id = DvdDiscId.Compute(videoTs, new byte[]?[] { vts01, null, vts03 });

        await Assert.That(id).IsEqualTo(ExpectedMd5(videoTs, vts01, vts03));
    }

    [Test]
    public async Task Compute_ResultIsThirtyTwoUppercaseHexChars()
    {
        var id = DvdDiscId.Compute(MakeVideoTsIfo(0), Array.Empty<byte[]?>());

        await Assert.That(id.Length).IsEqualTo(32);
        await Assert.That(id).IsEqualTo(id.ToUpperInvariant());
    }

    [Test]
    public async Task Compute_NullVideoTs_Throws()
    {
        await Assert.That(() => DvdDiscId.Compute(null!, Array.Empty<byte[]?>()))
            .Throws<ArgumentNullException>();
    }

    [Test]
    public async Task Compute_NullVtsList_Throws()
    {
        await Assert.That(() => DvdDiscId.Compute(MakeVideoTsIfo(0), null!))
            .Throws<ArgumentNullException>();
    }
}
