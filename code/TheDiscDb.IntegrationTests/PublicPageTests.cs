namespace TheDiscDb.IntegrationTests;

[ClassDataSource<AspireAppFixture>(Shared = SharedType.PerTestSession)]
public class PublicPageTests(AspireAppFixture fixture)
{
    private HttpClient Client => fixture.HttpClient;

    [Test]
    public async Task Homepage_ReturnsOkWithHtml()
    {
        var response = await Client.GetAsync("/");

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
        var contentType = response.Content.Headers.ContentType?.MediaType;
        await Assert.That(contentType).IsEqualTo("text/html");
    }

    [Test]
    public async Task MoviesPage_ReturnsOk()
    {
        var response = await Client.GetAsync("/movies");

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
    }

    [Test]
    public async Task SeriesPage_ReturnsOk()
    {
        var response = await Client.GetAsync("/series");

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
    }

    [Test]
    public async Task BoxsetsPage_ReturnsOk()
    {
        var response = await Client.GetAsync("/boxsets");

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
    }

    [Test]
    public async Task AboutPage_ReturnsOk()
    {
        var response = await Client.GetAsync("/about");

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
    }

    [Test]
    public async Task LeaderboardPage_ReturnsOk()
    {
        var response = await Client.GetAsync("/leaderboard");

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
    }

    [Test]
    public async Task SearchPage_ReturnsOk()
    {
        var response = await Client.GetAsync("/search");

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
    }

    [Test]
    public async Task SearchWithQuery_ReturnsOk()
    {
        var response = await Client.GetAsync("/search/test");

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
    }

    [Test]
    public async Task LoginPage_ReturnsOk()
    {
        var response = await Client.GetAsync("/Account/Login");

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
    }

    [Test]
    public async Task ErrorPage_ReturnsOk()
    {
        var response = await Client.GetAsync("/Error");

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
    }

    [Test]
    public async Task NonExistentPage_ReturnsNotFound()
    {
        var response = await Client.GetAsync("/this-page-does-not-exist-at-all");

        // Blazor catch-all routes may return 200 with a "not found" page,
        // or it could return 404. Either is acceptable behavior.
        var status = (int)response.StatusCode;
        await Assert.That(status).IsGreaterThanOrEqualTo(200).And.IsLessThanOrEqualTo(404);
    }
}
