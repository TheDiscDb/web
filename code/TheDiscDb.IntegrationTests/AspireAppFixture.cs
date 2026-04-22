using Microsoft.Extensions.Logging;
using TUnit.Core.Interfaces;

namespace TheDiscDb.IntegrationTests;

public class AspireAppFixture : IAsyncInitializer, IAsyncDisposable
{
    private DistributedApplication? app;

    public HttpClient HttpClient { get; private set; } = default!;

    private static readonly TimeSpan StartupTimeout = TimeSpan.FromSeconds(120);

    public async Task InitializeAsync()
    {
        var builder = await DistributedApplicationTestingBuilder
            .CreateAsync<Projects.TheDiscDb_AppHost>();

        builder.Services.AddLogging(logging =>
        {
            logging.SetMinimumLevel(LogLevel.Information);
            logging.AddFilter("Aspire.", LogLevel.Warning);
        });

        builder.Services.ConfigureHttpClientDefaults(clientBuilder =>
        {
            clientBuilder.AddStandardResilienceHandler();
        });

        using var cts = new CancellationTokenSource(StartupTimeout);

        this.app = await builder.BuildAsync(cts.Token)
            .WaitAsync(StartupTimeout, cts.Token);

        await this.app.StartAsync(cts.Token)
            .WaitAsync(StartupTimeout, cts.Token);

        await this.app.ResourceNotifications
            .WaitForResourceHealthyAsync("thediscdb-web", cts.Token)
            .WaitAsync(StartupTimeout, cts.Token);

        this.HttpClient = this.app.CreateHttpClient("thediscdb-web");
    }

    public async ValueTask DisposeAsync()
    {
        if (this.app != null)
        {
            await this.app.DisposeAsync();
        }

        GC.SuppressFinalize(this);
    }
}
