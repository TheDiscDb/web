namespace TheDiscDb.UnitTests.Core.DiscHash;

using TheDiscDb.Core.DiscHash;

public class AacsDiscIdTests
{
    // SHA1("hello") = AAF4C61DDCC5E8A2DABEDE0F3B482CD9AEA9434D
    // Verified against: https://emn178.github.io/online-tools/sha1.html and RFC 3174.
    [Test]
    public async Task Compute_WithKnownBytes_ReturnsExpectedUppercaseHex()
    {
        var bytes = "hello"u8.ToArray();

        var id = AacsDiscId.Compute(bytes);

        await Assert.That(id).IsEqualTo("AAF4C61DDCC5E8A2DABEDE0F3B482CD9AEA9434D");
    }

    // SHA1("") = DA39A3EE5E6B4B0D3255BFEF95601890AFD80709 (FIPS 180-4 example)
    [Test]
    public async Task Compute_EmptyBytes_ReturnsKnownSha1OfEmpty()
    {
        var id = AacsDiscId.Compute(Array.Empty<byte>());

        await Assert.That(id).IsEqualTo("DA39A3EE5E6B4B0D3255BFEF95601890AFD80709");
    }

    [Test]
    public async Task Compute_StreamOverload_MatchesByteArrayOverload()
    {
        var bytes = new byte[] { 0xDE, 0xAD, 0xBE, 0xEF, 0xCA, 0xFE };

        var fromBytes  = AacsDiscId.Compute(bytes);
        var fromStream = AacsDiscId.Compute(new MemoryStream(bytes));

        await Assert.That(fromStream).IsEqualTo(fromBytes);
    }

    [Test]
    public async Task Compute_ResultIsAlwaysUppercase()
    {
        var bytes = new byte[] { 0x00, 0xff, 0x10, 0x20, 0x30 };

        var id = AacsDiscId.Compute(bytes);

        await Assert.That(id).IsEqualTo(id.ToUpperInvariant());
    }

    [Test]
    public async Task Compute_ResultIsExactly40Chars()
    {
        var id = AacsDiscId.Compute(new byte[] { 1, 2, 3 });

        await Assert.That(id.Length).IsEqualTo(40);
    }

    // Null-guard tests
    [Test]
    public async Task Compute_NullByteArray_Throws()
    {
        await Assert.That(() => AacsDiscId.Compute((byte[])null!))
            .Throws<ArgumentNullException>();
    }

    [Test]
    public async Task Compute_NullStream_Throws()
    {
        await Assert.That(() => AacsDiscId.Compute((Stream)null!))
            .Throws<ArgumentNullException>();
    }

    // Real-disc validation test.
    // Requires the "Big Miracle" (2012) Blu-ray disc to be mounted at D:\.
    // Reference ID was captured via PowerShell SHA1 directly on D:\AACS\Unit_Key_RO.inf.
    // Skip attribute removed once the disc is in the drive to validate Phase 0.
    [Test]
    [Skip("Requires 'Big Miracle' Blu-ray in drive D:\\")]
    public async Task Compute_BigMiracle_ReturnsKnownDiscId()
    {
        const string path = @"D:\AACS\Unit_Key_RO.inf";
        const string expected = "F89B83B6986D79571C222CE533D4126FB1ECFA9A";

        var bytes = File.ReadAllBytes(path);
        var id = AacsDiscId.Compute(bytes);

        await Assert.That(id).IsEqualTo(expected);
    }
}
