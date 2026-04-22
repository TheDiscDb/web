namespace TheDiscDb.IntegrationTests;

/// <summary>
/// Tests for the /api/contribute/* REST endpoints.
/// These endpoints require authentication, so we test that unauthenticated
/// requests are properly rejected.
/// </summary>
[ClassDataSource<AspireAppFixture>(Shared = SharedType.PerTestSession)]
public class ContributionEndpointTests(AspireAppFixture fixture)
{
    private HttpClient Client => fixture.HttpClient;

    [Test]
    public async Task ExternalSearch_Movie_WithoutAuth_ReturnsUnauthorizedOrRedirect()
    {
        var handler = new HttpClientHandler { AllowAutoRedirect = false };
        using var client = new HttpClient(handler) { BaseAddress = Client.BaseAddress };

        var response = await client.GetAsync("/api/contribute/externalsearch/movie?query=test");

        // RequireAuthorization on the group — should be 401 or 302 redirect to login
        var status = (int)response.StatusCode;
        await Assert.That(status is 401 or 302).IsTrue();
    }

    [Test]
    public async Task UploadImage_WithoutAuth_ReturnsUnauthorizedOrRedirect()
    {
        var handler = new HttpClientHandler { AllowAutoRedirect = false };
        using var client = new HttpClient(handler) { BaseAddress = Client.BaseAddress };

        var fakeId = Guid.NewGuid();
        using var form = new MultipartFormDataContent();
        form.Add(new ByteArrayContent([0xFF, 0xD8]), "file", "test.jpg");

        var response = await client.PostAsync($"/api/contribute/images/front/upload/{fakeId}", form);

        var status = (int)response.StatusCode;
        await Assert.That(status is 401 or 302).IsTrue();
    }

    [Test]
    public async Task UploadContributionLogs_WithAllowAnonymous_AcceptsRequest()
    {
        // This endpoint has [AllowAnonymous], so it should accept unauthenticated requests
        // but may fail with 404 for a non-existent contribution ID
        var fakeContributionId = "nonexistent";
        var fakeDiscId = "nonexistent";
        var content = new StringContent("test log data", System.Text.Encoding.UTF8, "text/plain");

        var response = await Client.PostAsync(
            $"/api/contribute/{fakeContributionId}/discs/{fakeDiscId}/logs", content);

        // AllowAnonymous means we should NOT get 401 — we should get 404 or 400
        var status = (int)response.StatusCode;
        await Assert.That(status).IsNotEqualTo(401);
    }

    [Test]
    public async Task DeleteLogError_WithAllowAnonymous_AcceptsRequest()
    {
        var fakeContributionId = "nonexistent";
        var fakeDiscId = "nonexistent";

        var response = await Client.DeleteAsync(
            $"/api/contribute/{fakeContributionId}/discs/{fakeDiscId}/logs/error");

        var status = (int)response.StatusCode;
        await Assert.That(status).IsNotEqualTo(401);
    }

    [Test]
    public async Task ServeContributionImage_ReturnsNotFoundForMissing()
    {
        var response = await Client.GetAsync("/api/contribute/images/nonexistent/path.jpg");

        // The image proxy should handle this gracefully
        var status = (int)response.StatusCode;
        await Assert.That(status).IsLessThan(500);
    }
}
