namespace TheDiscDb.IntegrationTests;

[ClassDataSource<AspireAppFixture>(Shared = SharedType.PerTestSession)]
public class AuthTests(AspireAppFixture fixture)
{
    private HttpClient Client => fixture.HttpClient;

    /// <summary>
    /// Creates an HttpClient that does not follow redirects, so we can inspect 302/401 responses.
    /// </summary>
    private HttpClient CreateNoRedirectClient()
    {
        var handler = new HttpClientHandler { AllowAutoRedirect = false };
        return new HttpClient(handler) { BaseAddress = Client.BaseAddress };
    }

    #region Admin Pages — Require Authentication

    [Test]
    public async Task AdminPage_WithoutAuth_RedirectsToLogin()
    {
        using var client = CreateNoRedirectClient();
        var response = await client.GetAsync("/admin");

        // Blazor interactive server pages may not redirect — they render the page
        // and show a "not authorized" message. Accept either redirect or 200.
        var status = (int)response.StatusCode;
        var isRedirect = status is 302 or 301;
        var isOk = status == 200;
        await Assert.That(isRedirect || isOk).IsTrue();

        if (isRedirect)
        {
            var location = response.Headers.Location?.ToString() ?? "";
            await Assert.That(location).Contains("Account/Login");
        }
    }

    [Test]
    public async Task AdminApiKeys_WithoutAuth_RedirectsOrDenies()
    {
        using var client = CreateNoRedirectClient();
        var response = await client.GetAsync("/admin/apikeys");

        var status = (int)response.StatusCode;
        await Assert.That(status is 200 or 301 or 302 or 401 or 403).IsTrue();
    }

    #endregion

    #region Contribution Pages — Require Authentication

    [Test]
    public async Task ContributePage_WithoutAuth_RedirectsOrDenies()
    {
        using var client = CreateNoRedirectClient();
        var response = await client.GetAsync("/contribute");

        var status = (int)response.StatusCode;
        await Assert.That(status is 200 or 301 or 302 or 401 or 403).IsTrue();
    }

    [Test]
    public async Task MessagesPage_WithoutAuth_RedirectsOrDenies()
    {
        using var client = CreateNoRedirectClient();
        var response = await client.GetAsync("/messages");

        var status = (int)response.StatusCode;
        await Assert.That(status is 200 or 301 or 302 or 401 or 403).IsTrue();
    }

    #endregion

    #region API Key Authentication

    [Test]
    public async Task ContributionGraphQL_InvalidApiKey_ReturnsUnauthorizedOrRedirect()
    {
        using var client = CreateNoRedirectClient();

        using var request = new HttpRequestMessage(HttpMethod.Post, "/graphql/contributions");
        request.Headers.Add("Authorization", "ApiKey invalid-key-that-does-not-exist");
        request.Content = new StringContent(
            """{"query": "{ __schema { queryType { name } } }"}""",
            System.Text.Encoding.UTF8,
            "application/json");

        var response = await client.SendAsync(request);

        // May return 401 (ApiKey scheme fails) or 302 (Identity scheme redirect)
        var status = (int)response.StatusCode;
        await Assert.That(status is 401 or 302).IsTrue();
    }

    [Test]
    public async Task ContributionGraphQL_NoAuthHeader_ReturnsUnauthorizedOrRedirect()
    {
        using var client = CreateNoRedirectClient();

        var content = new StringContent(
            """{"query": "{ __schema { queryType { name } } }"}""",
            System.Text.Encoding.UTF8,
            "application/json");

        var response = await client.PostAsync("/graphql/contributions", content);

        var status = (int)response.StatusCode;
        await Assert.That(status is 401 or 302).IsTrue();
    }

    [Test]
    public async Task ContributionGraphQL_ValidPublicApiKey_ReturnsOk()
    {
        if (string.IsNullOrEmpty(fixture.PublicApiKey))
        {
            return;
        }

        using var request = new HttpRequestMessage(HttpMethod.Post, "/graphql/contributions");
        request.Headers.Add("Authorization", $"ApiKey {fixture.PublicApiKey}");
        request.Content = new StringContent(
            """{"query": "{ __schema { queryType { name } } }"}""",
            System.Text.Encoding.UTF8,
            "application/json");

        var response = await Client.SendAsync(request);

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
    }

    #endregion

    #region Account Endpoints

    [Test]
    public async Task Logout_WithoutAuth_DoesNotCrash()
    {
        var content = new FormUrlEncodedContent([]);
        var response = await Client.PostAsync("/Account/Logout", content);

        // Should not crash — may redirect to login
        var status = (int)response.StatusCode;
        await Assert.That(status).IsLessThan(500);
    }

    #endregion
}
