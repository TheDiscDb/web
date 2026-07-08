namespace TheDiscDb.UnitTests.Core.DiscHash;

using System.Security.Cryptography;
using TheDiscDb.Core.DiscHash;

public class Md5DigestTests
{
    private static string Hex(byte[] b) => Convert.ToHexString(b).ToLowerInvariant();

    // RFC 1321 test suite vectors.
    [Test]
    [Arguments("", "d41d8cd98f00b204e9800998ecf8427e")]
    [Arguments("a", "0cc175b9c0f1b6a831c399e269772661")]
    [Arguments("abc", "900150983cd24fb0d6963f7d28e17f72")]
    [Arguments("message digest", "f96b697d7cb7938d525a2f31aaf161d0")]
    [Arguments("abcdefghijklmnopqrstuvwxyz", "c3fcd3d76192e4007dfb496cca67e13b")]
    public async Task HashData_MatchesRfc1321Vectors(string input, string expected)
    {
        var hash = Md5Digest.HashData(System.Text.Encoding.ASCII.GetBytes(input));

        await Assert.That(Hex(hash)).IsEqualTo(expected);
    }

    [Test]
    public async Task HashData_QuickBrownFox()
    {
        var hash = Md5Digest.HashData(
            System.Text.Encoding.ASCII.GetBytes("The quick brown fox jumps over the lazy dog"));

        await Assert.That(Hex(hash)).IsEqualTo("9e107d9d372bb6826bd81d3542a419d6");
    }

    [Test]
    public async Task Incremental_MatchesOneShot_AcrossBlockBoundaries()
    {
        var data = new byte[1000];
        new Random(12345).NextBytes(data);

        var digest = new Md5Digest();
        int offset = 0;
        foreach (var chunk in new[] { 1, 63, 64, 65, 100, 200, 507 })
        {
            digest.Append(data.AsSpan(offset, chunk));
            offset += chunk;
        }

        var incremental = Hex(digest.Finish());
        var expected = Hex(MD5.HashData(data));

        await Assert.That(incremental).IsEqualTo(expected);
    }

    // Cross-check managed MD5 against the BCL for many sizes (including 55/56/64-byte
    // padding edge cases where the length spills into an extra block).
    [Test]
    public async Task HashData_MatchesBclAcrossSizes()
    {
        var rng = new Random(98765);
        foreach (var size in new[] { 0, 1, 55, 56, 57, 63, 64, 65, 127, 128, 129, 1024, 4096 })
        {
            var data = new byte[size];
            rng.NextBytes(data);

            var managed = Hex(Md5Digest.HashData(data));
            var bcl = Hex(MD5.HashData(data));

            await Assert.That(managed).IsEqualTo(bcl);
        }
    }

    [Test]
    public async Task Finish_Twice_Throws()
    {
        var digest = new Md5Digest();
        digest.Append([1, 2, 3]);
        digest.Finish();

        await Assert.That(() => digest.Finish()).Throws<InvalidOperationException>();
    }
}
