namespace TheDiscDb.IntegrationTests;

[ClassDataSource<AspireAppFixture>(Shared = SharedType.PerTestSession)]
public class HealthCheckTests(AspireAppFixture fixture)
{
    private HttpClient Client => fixture.HttpClient;

    [Test]
    public async Task HealthEndpoint_ReturnsHealthy()
    {
        var response = await Client.GetAsync("/health");

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
        var body = await response.Content.ReadAsStringAsync();
        await Assert.That(body).IsEqualTo("Healthy");
    }

    [Test]
    public async Task AliveEndpoint_ReturnsHealthy()
    {
        var response = await Client.GetAsync("/alive");

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
        var body = await response.Content.ReadAsStringAsync();
        await Assert.That(body).IsEqualTo("Healthy");
    }
}
