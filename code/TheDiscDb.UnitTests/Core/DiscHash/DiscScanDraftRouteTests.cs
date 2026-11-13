namespace TheDiscDb.UnitTests.Core.DiscHash;

using TheDiscDb.Core.DiscHash;

public class DiscScanDraftRouteTests
{
    [Test]
    [Arguments("test")]
    [Arguments("a/b?c#d%2F")]
    [Arguments("../..")]
    [Arguments("manual-74657374")]
    [Arguments("Test With Spaces & Symbols +")]
    [Arguments("caf\u00e9/\u65e5\u672c")]
    public async Task ManualHash_RoundTripsThroughLowercaseSafePath(string hash)
    {
        string route = DiscScanDraft.EncodeHashForRoute(hash);

        await Assert.That(route.All(character => character == '-' || char.IsAsciiLetterOrDigit(character))).IsTrue();
        await Assert.That(route).IsEqualTo(route.ToLowerInvariant());
        await Assert.That(DiscScanDraft.TryDecodeHashFromRoute(route, out string decoded)).IsTrue();
        await Assert.That(decoded).IsEqualTo(hash);
        await Assert.That(new Uri($"https://localhost/contribute/disc/{route}").AbsolutePath)
            .IsEqualTo($"/contribute/disc/{route}");
    }

    [Test]
    public async Task LongManualHash_IsNotTruncated()
    {
        string hash = new('X', 1024);
        string route = DiscScanDraft.EncodeHashForRoute(hash);

        await Assert.That(DiscScanDraft.TryDecodeHashFromRoute(route, out string decoded)).IsTrue();
        await Assert.That(decoded).IsEqualTo(hash);
    }

    [Test]
    public async Task RealHash_KeepsExistingRoute()
    {
        const string hash = "0123456789ABCDEF0123456789ABCDEF";

        await Assert.That(DiscScanDraft.EncodeHashForRoute(hash)).IsEqualTo(hash.ToLowerInvariant());
        await Assert.That(DiscScanDraft.TryDecodeHashFromRoute(hash, out string decoded)).IsTrue();
        await Assert.That(decoded).IsEqualTo(hash);
    }

    [Test]
    [Arguments("manual-xyz")]
    [Arguments("manual-1")]
    public async Task InvalidRoute_IsRejected(string route)
    {
        await Assert.That(DiscScanDraft.TryDecodeHashFromRoute(route, out _)).IsFalse();
    }
}
