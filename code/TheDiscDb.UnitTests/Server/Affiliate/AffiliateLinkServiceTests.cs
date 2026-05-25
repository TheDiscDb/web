using Microsoft.Extensions.Options;
using TheDiscDb.Affiliate;

namespace TheDiscDb.UnitTests.Server.Affiliate;

public class AffiliateLinkServiceTests
{
    private static AffiliateLinkService CreateService(
        string? pid = "12345",
        string? advertiserId = "67890",
        string? cjTrackingDomain = null,
        string? utmSource = "thediscdb",
        string? utmMedium = "referral",
        string? utmCampaign = "release")
    {
        var options = Options.Create(new AffiliateLinkOptions
        {
            Pid = pid,
            AdvertiserId = advertiserId,
            CjTrackingDomain = cjTrackingDomain,
            UtmSource = utmSource,
            UtmMedium = utmMedium,
            UtmCampaign = utmCampaign,
        });
        return new AffiliateLinkService(options);
    }

    [Test]
    public async Task Decorate_EmptyUrl_ReturnsEmpty()
    {
        var service = CreateService();
        var result = service.Decorate(null);
        await Assert.That(result).IsEqualTo(string.Empty);
    }

    [Test]
    public async Task Decorate_RelativeUrl_ReturnsEmpty()
    {
        // URL allowlist: relative URLs fail Uri.TryCreate(Absolute) and are rejected.
        var service = CreateService();
        var result = service.Decorate("/products/jaws");
        await Assert.That(result).IsEqualTo(string.Empty);
    }

    [Test]
    public async Task Decorate_JavascriptScheme_ReturnsEmpty()
    {
        var service = CreateService();
        var result = service.Decorate("javascript:alert(1)");
        await Assert.That(result).IsEqualTo(string.Empty);
    }

    [Test]
    public async Task Decorate_DataScheme_ReturnsEmpty()
    {
        var service = CreateService();
        var result = service.Decorate("data:text/html,<script>alert(1)</script>");
        await Assert.That(result).IsEqualTo(string.Empty);
    }

    [Test]
    public async Task Decorate_HttpScheme_ReturnsEmpty()
    {
        // We only allowlist https://gruv.com — bare http is rejected even for gruv.com.
        var service = CreateService();
        var result = service.Decorate("http://gruv.com/products/jaws");
        await Assert.That(result).IsEqualTo(string.Empty);
    }

    [Test]
    public async Task Decorate_OtherHost_ReturnsEmpty()
    {
        var service = CreateService();
        var result = service.Decorate("https://evil.example.com/products/jaws");
        await Assert.That(result).IsEqualTo(string.Empty);
    }

    [Test]
    public async Task Decorate_WwwGruvCom_IsAllowed()
    {
        var service = CreateService();
        var result = service.Decorate("https://www.gruv.com/products/jaws");
        await Assert.That(result).Contains("click-12345-67890");
    }

    [Test]
    public async Task Decorate_WithCjCredentials_WrapsInCjDeepLink()
    {
        var service = CreateService(pid: "12345", advertiserId: "67890");
        var result = service.Decorate("https://gruv.com/products/jaws-4k-uhd-blu-ray");

        // CJ deep-link shape: https://<trackingDomain>/click-<pid>-<aid>?url=<encodedDestination>
        await Assert.That(result).StartsWith("https://www.anrdoezrs.net/click-12345-67890?url=");
        // The encoded destination should include the UTM tags merged onto the original URL
        await Assert.That(result).Contains("utm_source%3Dthediscdb");
        await Assert.That(result).Contains("utm_medium%3Dreferral");
        await Assert.That(result).Contains("utm_campaign%3Drelease");
    }

    [Test]
    public async Task Decorate_WithSid_AppendsSidQueryParam()
    {
        var service = CreateService();
        var result = service.Decorate("https://gruv.com/products/jaws", sid: "release-detail");
        await Assert.That(result).Contains("&sid=release-detail");
    }

    [Test]
    public async Task Decorate_WithoutSid_OmitsSidQueryParam()
    {
        var service = CreateService();
        var result = service.Decorate("https://gruv.com/products/jaws");
        await Assert.That(result).DoesNotContain("sid=");
    }

    [Test]
    public async Task Decorate_MissingPid_DegradesToUtmOnly()
    {
        var service = CreateService(pid: null, advertiserId: "67890");
        var result = service.Decorate("https://gruv.com/products/jaws");

        // No CJ redirect — just the destination URL with UTM tags
        await Assert.That(result).StartsWith("https://gruv.com/products/jaws?");
        await Assert.That(result).Contains("utm_source=thediscdb");
    }

    [Test]
    public async Task Decorate_MissingAdvertiserId_DegradesToUtmOnly()
    {
        var service = CreateService(pid: "12345", advertiserId: null);
        var result = service.Decorate("https://gruv.com/products/jaws");

        await Assert.That(result).StartsWith("https://gruv.com/products/jaws?");
        await Assert.That(result).Contains("utm_source=thediscdb");
    }

    [Test]
    public async Task Decorate_CustomTrackingDomain_UsesIt()
    {
        var service = CreateService(cjTrackingDomain: "track.example.com");
        var result = service.Decorate("https://gruv.com/products/jaws");
        await Assert.That(result).StartsWith("https://track.example.com/click-");
    }

    [Test]
    public async Task Decorate_UrlWithExistingQueryString_AppendsUtmWithAmpersand()
    {
        var service = CreateService(pid: null, advertiserId: null);
        var result = service.Decorate("https://gruv.com/products/jaws?variant=42");
        await Assert.That(result).IsEqualTo("https://gruv.com/products/jaws?variant=42&utm_source=thediscdb&utm_medium=referral&utm_campaign=release");
    }

    [Test]
    public async Task Decorate_UrlWithFragment_PreservesFragmentAfterUtm()
    {
        var service = CreateService(pid: null, advertiserId: null);
        var result = service.Decorate("https://gruv.com/products/jaws#reviews");
        await Assert.That(result).EndsWith("#reviews");
        await Assert.That(result).Contains("utm_source=thediscdb");
    }

    [Test]
    public async Task Decorate_NoUtmConfigured_ReturnsOriginalUrlInCjWrapper()
    {
        var service = CreateService(utmSource: null, utmMedium: null, utmCampaign: null);
        var result = service.Decorate("https://gruv.com/products/jaws");

        await Assert.That(result).StartsWith("https://www.anrdoezrs.net/click-12345-67890?url=");
        await Assert.That(result).DoesNotContain("utm_");
    }
}
