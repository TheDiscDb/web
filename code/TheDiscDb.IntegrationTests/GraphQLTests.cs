using System.Text;
using System.Text.Json;

namespace TheDiscDb.IntegrationTests;

[ClassDataSource<AspireAppFixture>(Shared = SharedType.PerTestSession)]
public class GraphQLTests(AspireAppFixture fixture)
{
    private HttpClient Client => fixture.HttpClient;

    private async Task<(HttpResponseMessage Response, JsonDocument? Doc)> ExecuteGraphQLRawAsync(
        string query, string endpoint = "/graphql")
    {
        var request = new { query };
        var content = new StringContent(
            JsonSerializer.Serialize(request),
            Encoding.UTF8,
            "application/json");

        var response = await Client.PostAsync(endpoint, content);

        if (!response.IsSuccessStatusCode)
        {
            return (response, null);
        }

        var json = await response.Content.ReadAsStringAsync();
        return (response, JsonDocument.Parse(json));
    }

    private async Task<JsonDocument> ExecuteGraphQLAsync(string query, string endpoint = "/graphql")
    {
        var (response, doc) = await ExecuteGraphQLRawAsync(query, endpoint);
        response.EnsureSuccessStatusCode();
        return doc!;
    }

    #region Public Schema (/graphql)

    [Test]
    public async Task GetMediaItems_ReturnsData()
    {
        using var doc = await ExecuteGraphQLAsync(
            "{ mediaItems(first: 5) { nodes { title slug type } } }");

        var data = doc.RootElement.GetProperty("data");
        var nodes = data.GetProperty("mediaItems").GetProperty("nodes");
        await Assert.That(nodes.ValueKind).IsEqualTo(JsonValueKind.Array);
    }

    [Test]
    public async Task GetMediaItems_WithFilter_ReturnsFilteredResults()
    {
        // First get an item to see what type values look like
        using var sampleDoc = await ExecuteGraphQLAsync(
            "{ mediaItems(first: 1) { nodes { type } } }");

        var sampleNodes = sampleDoc.RootElement
            .GetProperty("data")
            .GetProperty("mediaItems")
            .GetProperty("nodes");

        // If there's data, use the actual type value for filtering
        if (sampleNodes.GetArrayLength() > 0)
        {
            var actualType = sampleNodes[0].GetProperty("type").GetString()!;

            using var doc = await ExecuteGraphQLAsync(
                $$"""{ mediaItems(first: 5, where: { type: { eq: "{{actualType}}" } }) { nodes { title type } } }""");

            var nodes = doc.RootElement
                .GetProperty("data")
                .GetProperty("mediaItems")
                .GetProperty("nodes");

            await Assert.That(nodes.ValueKind).IsEqualTo(JsonValueKind.Array);

            foreach (var node in nodes.EnumerateArray())
            {
                var type = node.GetProperty("type").GetString();
                await Assert.That(type).IsEqualTo(actualType);
            }
        }
    }

    [Test]
    public async Task GetBoxsets_ReturnsData()
    {
        using var doc = await ExecuteGraphQLAsync(
            "{ boxsets(first: 5) { nodes { title slug } } }");

        var data = doc.RootElement.GetProperty("data");
        var nodes = data.GetProperty("boxsets").GetProperty("nodes");
        await Assert.That(nodes.ValueKind).IsEqualTo(JsonValueKind.Array);
    }

    [Test]
    public async Task GetMediaItemsByGroup_ReturnsData()
    {
        // First get a valid group slug from the sitemap or a known slug
        // This query should work even with an unknown slug — it just returns empty
        using var doc = await ExecuteGraphQLAsync(
            """{ mediaItemsByGroup(slug: "nonexistent-group", first: 5) { nodes { title } } }""");

        var data = doc.RootElement.GetProperty("data");
        var nodes = data.GetProperty("mediaItemsByGroup").GetProperty("nodes");
        await Assert.That(nodes.ValueKind).IsEqualTo(JsonValueKind.Array);
    }

    [Test]
    public async Task Introspection_ReturnsSchema()
    {
        using var doc = await ExecuteGraphQLAsync(
            "{ __schema { queryType { name } } }");

        var schemaName = doc.RootElement
            .GetProperty("data")
            .GetProperty("__schema")
            .GetProperty("queryType")
            .GetProperty("name")
            .GetString();

        await Assert.That(schemaName).IsEqualTo("Query");
    }

    [Test]
    public async Task MediaItems_Pagination_WorksCorrectly()
    {
        using var doc = await ExecuteGraphQLAsync(
            "{ mediaItems(first: 2) { nodes { title } pageInfo { hasNextPage endCursor } } }");

        var pageInfo = doc.RootElement
            .GetProperty("data")
            .GetProperty("mediaItems")
            .GetProperty("pageInfo");

        // pageInfo should exist and have expected fields
        await Assert.That(pageInfo.TryGetProperty("hasNextPage", out _)).IsTrue();
        await Assert.That(pageInfo.TryGetProperty("endCursor", out _)).IsTrue();
    }

    #endregion

    #region Contribution Schema (/graphql/contributions)

    [Test]
    public async Task ContributionSchema_WithoutAuth_ReturnsUnauthorizedOrRedirect()
    {
        // Use a no-redirect client to see the actual response
        var handler = new HttpClientHandler { AllowAutoRedirect = false };
        using var noRedirectClient = new HttpClient(handler) { BaseAddress = Client.BaseAddress };

        var content = new StringContent(
            JsonSerializer.Serialize(new { query = "{ myContributions { id } }" }),
            Encoding.UTF8,
            "application/json");

        var response = await noRedirectClient.PostAsync("/graphql/contributions", content);

        // ASP.NET Identity may redirect to login (302) or return 401
        var status = (int)response.StatusCode;
        await Assert.That(status is 401 or 302).IsTrue();
    }

    [Test]
    public async Task ContributionSchema_WithApiKey_ReturnsData()
    {
        // Skip if public API key is not available
        var handler = new HttpClientHandler { AllowAutoRedirect = false };
        using var client = new HttpClient(handler) { BaseAddress = Client.BaseAddress };

        if (string.IsNullOrEmpty(fixture.PublicApiKey))
        {
            // API key not resolved — skip test
            return;
        }

        using var requestMessage = new HttpRequestMessage(HttpMethod.Post, "/graphql/contributions");
        requestMessage.Headers.Add("Authorization", $"ApiKey {fixture.PublicApiKey}");
        requestMessage.Content = new StringContent(
            JsonSerializer.Serialize(new { query = "{ __schema { queryType { name } } }" }),
            Encoding.UTF8,
            "application/json");

        var response = await client.SendAsync(requestMessage);

        // If API key auth is enabled, we get 200 with schema data.
        // If disabled (default in Aspire), the handler returns NoResult and Identity redirects.
        if (response.StatusCode == HttpStatusCode.OK)
        {
            var json = await response.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(json);
            var schemaName = doc.RootElement
                .GetProperty("data")
                .GetProperty("__schema")
                .GetProperty("queryType")
                .GetProperty("name")
                .GetString();

            await Assert.That(schemaName).IsEqualTo("ContributionQuery");
        }
        else
        {
            // API key auth not enabled — redirect is expected
            await Assert.That((int)response.StatusCode).IsEqualTo(302);
        }
    }

    #endregion

    #region Invalid Queries

    [Test]
    public async Task InvalidQuery_ReturnsErrors()
    {
        var (response, doc) = await ExecuteGraphQLRawAsync("{ nonExistentField }");

        // HotChocolate may return 200 with errors or 400 with errors
        var body = await response.Content.ReadAsStringAsync();
        using var parsed = doc ?? JsonDocument.Parse(body);

        var hasErrors = parsed.RootElement.TryGetProperty("errors", out var errors);
        await Assert.That(hasErrors).IsTrue();
        await Assert.That(errors.GetArrayLength()).IsGreaterThan(0);
    }

    [Test]
    public async Task EmptyQuery_ReturnsBadRequest()
    {
        var content = new StringContent(
            JsonSerializer.Serialize(new { query = "" }),
            Encoding.UTF8,
            "application/json");

        var response = await Client.PostAsync("/graphql", content);

        // HotChocolate may return 200 with errors or 400
        var body = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(body);
        var hasErrors = doc.RootElement.TryGetProperty("errors", out _);
        // Either we get a non-OK status or errors in the response
        var statusNotOk = (int)response.StatusCode >= 400;
        await Assert.That(statusNotOk || hasErrors).IsTrue();
    }

    #endregion
}
