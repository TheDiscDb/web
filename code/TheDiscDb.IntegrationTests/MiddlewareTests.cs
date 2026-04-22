using System.Xml.Linq;

namespace TheDiscDb.IntegrationTests;

[ClassDataSource<AspireAppFixture>(Shared = SharedType.PerTestSession)]
public class MiddlewareTests(AspireAppFixture fixture)
{
    private HttpClient Client => fixture.HttpClient;

    #region Sitemap

    [Test]
    public async Task SitemapXml_ReturnsValidXml()
    {
        var response = await Client.GetAsync("/sitemap.xml");

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
        await Assert.That(response.Content.Headers.ContentType?.MediaType).IsEqualTo("application/xml");

        var content = await response.Content.ReadAsStringAsync();
        var doc = XDocument.Parse(content);
        await Assert.That(doc.Root?.Name.LocalName).IsEqualTo("urlset");
    }

    [Test]
    public async Task GroupsXml_ReturnsValidXml()
    {
        var response = await Client.GetAsync("/groups.xml");

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
        await Assert.That(response.Content.Headers.ContentType?.MediaType).IsEqualTo("application/xml");

        var content = await response.Content.ReadAsStringAsync();
        var doc = XDocument.Parse(content);
        await Assert.That(doc.Root?.Name.LocalName).IsEqualTo("urlset");
    }

    [Test]
    public async Task RobotsTxt_ReturnsTextWithSitemapReferences()
    {
        var response = await Client.GetAsync("/robots.txt");

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
        await Assert.That(response.Content.Headers.ContentType?.MediaType).IsEqualTo("text/plain");

        var content = await response.Content.ReadAsStringAsync();
        await Assert.That(content).Contains("User-agent:");
        await Assert.That(content).Contains("sitemap.xml");
        await Assert.That(content).Contains("groups.xml");
    }

    #endregion

    #region RSS

    [Test]
    public async Task RssFeed_ReturnsOkResponse()
    {
        // The RSS feed may produce large or streaming responses.
        // Use a completion option to just read headers first.
        using var request = new HttpRequestMessage(HttpMethod.Get, "/rss");
        var response = await Client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead);

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
    }

    #endregion

    #region Lowercase URL Middleware

    [Test]
    public async Task UppercasePath_RedirectsToLowercase()
    {
        // Use a non-redirect-following client for this test
        var handler = new HttpClientHandler { AllowAutoRedirect = false };
        using var noRedirectClient = new HttpClient(handler) { BaseAddress = Client.BaseAddress };

        var response = await noRedirectClient.GetAsync("/Movies");

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.MovedPermanently);
        var location = response.Headers.Location?.ToString();
        await Assert.That(location).IsNotNull();
        await Assert.That(location!).Contains("/movies");
    }

    [Test]
    public async Task LowercasePath_DoesNotRedirect()
    {
        var handler = new HttpClientHandler { AllowAutoRedirect = false };
        using var noRedirectClient = new HttpClient(handler) { BaseAddress = Client.BaseAddress };

        var response = await noRedirectClient.GetAsync("/movies");

        // Should NOT be a redirect — should pass through
        await Assert.That((int)response.StatusCode).IsNotEqualTo(301);
    }

    [Test]
    public async Task CaseSensitivePath_Contribution_DoesNotRedirect()
    {
        // /contribution paths are case-sensitive (contain Sqids)
        var handler = new HttpClientHandler { AllowAutoRedirect = false };
        using var noRedirectClient = new HttpClient(handler) { BaseAddress = Client.BaseAddress };

        var response = await noRedirectClient.GetAsync("/contribution/ABC123");

        // Should NOT be a lowercase redirect (it's case-sensitive)
        await Assert.That((int)response.StatusCode).IsNotEqualTo(301);
    }

    [Test]
    public async Task StaticFileInPageUrl_Returns404()
    {
        // Simulates a cached page with relative asset references
        var handler = new HttpClientHandler { AllowAutoRedirect = false };
        using var noRedirectClient = new HttpClient(handler) { BaseAddress = Client.BaseAddress };

        var response = await noRedirectClient.GetAsync("/series/something/thediscdb.styles.css");

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.NotFound);
    }

    [Test]
    public async Task EmbeddedAssetPath_RedirectsToCorrectPath()
    {
        // Path like /movie/slug/_content/Foo/bar.js should redirect to /_content/Foo/bar.js
        var handler = new HttpClientHandler { AllowAutoRedirect = false };
        using var noRedirectClient = new HttpClient(handler) { BaseAddress = Client.BaseAddress };

        var response = await noRedirectClient.GetAsync("/movie/slug/_content/Foo/bar.js");

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.MovedPermanently);
        var location = response.Headers.Location?.ToString();
        await Assert.That(location).IsNotNull();
        await Assert.That(location!).Contains("/_content/Foo/bar.js");
    }

    #endregion

    #region WASM Config

    [Test]
    public async Task AppSettingsJson_ReturnsJson()
    {
        var response = await Client.GetAsync("/appsettings.json");

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
        await Assert.That(response.Content.Headers.ContentType?.MediaType).IsEqualTo("application/json");

        var content = await response.Content.ReadAsStringAsync();
        // Should be valid JSON
        var doc = System.Text.Json.JsonDocument.Parse(content);
        await Assert.That(doc).IsNotNull();
    }

    #endregion
}
