using Microsoft.Extensions.Logging;
using TUnit.Core.Interfaces;

namespace TheDiscDb.IntegrationTests;

public class AspireAppFixture : IAsyncInitializer, IAsyncDisposable
{
    private DistributedApplication? app;

    public HttpClient HttpClient { get; private set; } = default!;

    public string PublicApiKey { get; private set; } = string.Empty;

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

        // Read the auto-generated public API key from the AppHost project directory
        var appHostDir = Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory, "..", "..", "..", "..", "TheDiscDb.AppHost"));
        var keyFile = Path.Combine(appHostDir, ".public-apikey");
        if (File.Exists(keyFile))
        {
            this.PublicApiKey = (await File.ReadAllTextAsync(keyFile)).Trim();
        }

        // Also try reading from the AppHost's bin output as a fallback
        if (string.IsNullOrEmpty(this.PublicApiKey))
        {
            var altKeyFile = Path.Combine(appHostDir, "bin", "Debug", "net9.0", ".public-apikey");
            if (File.Exists(altKeyFile))
            {
                this.PublicApiKey = (await File.ReadAllTextAsync(altKeyFile)).Trim();
            }
        }
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
