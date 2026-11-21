using System.Net;
using System.Reflection;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;
using Microsoft.AspNetCore.Components.RenderTree;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using TheDiscDb.Client;
using TheDiscDb.Client.Contributions;
using TheDiscDb.Client.Pages.Contribute;
using TheDiscDb.Client.Services;
using TheDiscDb.OpticalDiscManifest.Generation;

namespace TheDiscDb.UnitTests.Client;

public class AddDiscTests
{
    [Test]
    [Arguments(HttpStatusCode.OK)]
    [Arguments(HttpStatusCode.ServiceUnavailable)]
    public async Task Scan_DetailsStayHiddenUntilDraftIsSaved(HttpStatusCode uploadStatus)
    {
        using var handler = new ScanHandler { UploadStatus = uploadStatus };
        using var services = CreateServices(handler);
        await using var renderer = new TestRenderer(services);
        using var component = CreateComponent(services, handler);
        renderer.Attach(component);

        Task scan = await renderer.Dispatcher.InvokeAsync(() => Task.FromResult(ScanAsync(component)));
        await handler.UploadStarted.Task.WaitAsync(TimeSpan.FromSeconds(10));
        try
        {
            await Assert.That(GetField<bool>(component, "discSelected")).IsFalse();
        }
        finally
        {
            handler.ResumeUpload.TrySetResult();
        }
        await scan;

        await Assert.That(GetField<bool>(component, "discSelected"))
            .IsEqualTo(uploadStatus == HttpStatusCode.OK);
        if (uploadStatus == HttpStatusCode.OK)
        {
            await renderer.Dispatcher.InvokeAsync(() => SubmitAsync(component));

            await Assert.That(handler.Attached).IsTrue();
            await Assert.That(new Uri(component.Navigation.Uri).AbsolutePath)
                .IsEqualTo("/contribution/Lyq/discs/disc/identify");
        }
        else
        {
            await Assert.That(GetField<string?>(component, "scanDraftHash")).IsNull();
            await Assert.That(GetField<string?>(component, "copyFlowError")).IsNotNull();
            await Assert.That(handler.Attached).IsFalse();
        }
    }

    [Test]
    public async Task HandleValidSubmit_StillScanning_DoesNotDiscardScanState()
    {
        using var component = new TestAddDisc
        {
            Configuration = new ConfigurationBuilder().AddInMemoryCollection(
                new Dictionary<string, string?> { ["Contributions:DiscFirstFlow"] = "true" }).Build()
        };
        SetField(component, "isScanning", true);
        SetField(component, "discSelected", true);

        await SubmitAsync(component);

        await Assert.That(GetField<bool>(component, "discSelected")).IsTrue();
        await Assert.That(GetField<string?>(component, "copyFlowError")).IsNull();
    }

    private static ServiceProvider CreateServices(ScanHandler handler)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddTheDiscDbClient().ConfigureHttpClient(
            client => client.BaseAddress = new Uri("https://localhost/graphql"),
            builder => builder.ConfigurePrimaryHttpMessageHandler(() => handler));
        services.AddContributionClient().ConfigureHttpClient(
            client => client.BaseAddress = new Uri("https://localhost/graphql/contributions"),
            builder => builder.ConfigurePrimaryHttpMessageHandler(() => handler));
        return services.BuildServiceProvider();
    }

    private static TestAddDisc CreateComponent(ServiceProvider services, ScanHandler handler)
    {
        var httpClient = new HttpClient(handler) { BaseAddress = new Uri("https://localhost/") };
        var component = new TestAddDisc
        {
            ContributionClient = services.GetRequiredService<IContributionClient>(),
            Query = services.GetRequiredService<GetDiscDetailByContentHashQuery>(),
            Navigation = new TestNavigation(),
            Configuration = new ConfigurationBuilder().AddInMemoryCollection(
                new Dictionary<string, string?> { ["Contributions:DiscFirstFlow"] = "true" }).Build(),
            ScanUploader = new DiscScanUploader(null!,
                new BrowserOpticalDiscManifestScanner(new OpticalDiscManifestGenerator()), httpClient)
        };
        typeof(AddDisc).GetProperty(nameof(AddDisc.ContributionId))!.SetValue(component, "Lyq");
        typeof(AddDisc).GetProperty("HttpClient", BindingFlags.Instance | BindingFlags.NonPublic)!
            .SetValue(component, httpClient);
        var request = GetField<SaveDiscRequest>(component, "request");
        request.Name = "Bonus Disc";
        request.Slug = "bonus-disc";
        return component;
    }

    private static Task ScanAsync(AddDisc component)
    {
        IReadOnlyList<DiscScanFile> files =
        [
            new DiscScanFile("BDMV/STREAM/00001.m2ts", "00001.m2ts", 1024, DateTime.UnixEpoch,
                (_, _) => throw new InvalidOperationException("Media payload must not be read."))
        ];
        return (Task)typeof(AddDisc).GetMethod("TryCalculateHash", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(component, [files])!;
    }

    private static Task SubmitAsync(AddDisc component)
        => (Task)typeof(AddDisc).GetMethod("HandleValidSubmit", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(component, null)!;

    private static T GetField<T>(AddDisc component, string name)
        => (T)typeof(AddDisc).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(component)!;

    private static void SetField(AddDisc component, string name, object value)
        => typeof(AddDisc).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(component, value);

    private sealed class ScanHandler : HttpMessageHandler
    {
        public HttpStatusCode UploadStatus { get; init; }
        public TaskCompletionSource UploadStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource ResumeUpload { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public bool Attached { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (request.RequestUri!.AbsolutePath.StartsWith("/api/", StringComparison.Ordinal))
            {
                if (request.RequestUri.AbsolutePath.EndsWith("/attach", StringComparison.Ordinal))
                {
                    this.Attached = true;
                    return new HttpResponseMessage(HttpStatusCode.OK);
                }

                this.UploadStarted.TrySetResult();
                await this.ResumeUpload.Task.WaitAsync(cancellationToken);
                return new HttpResponseMessage(this.UploadStatus);
            }

            using var body = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(cancellationToken));
            string operation = body.RootElement.GetProperty("operationName").GetString()!;
            string data = operation switch
            {
                "HashDisc" => """{"hashDisc":{"__typename":"HashDiscPayload","discHash":{"__typename":"DiscHash","hash":"ABC","fingerprint":"fingerprint"},"errors":null}}""",
                "GetDiscDetailByContentHash" => """{"mediaItems":{"__typename":"MediaItemsConnection","nodes":[]}}""",
                "GetIntakeDiscMatch" => """{"intakeDiscMatch":null}""",
                "CreateDisc" => """{"createDisc":{"__typename":"CreateDiscPayload","userContributionDisc":{"__typename":"UserContributionDisc","id":1,"encodedId":"disc"},"errors":null}}""",
                _ => throw new InvalidOperationException($"Unexpected GraphQL operation: {operation}")
            };
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent($$"""{"data":{{data}}}""", Encoding.UTF8, "application/json")
            };
        }
    }

    private sealed class TestAddDisc : AddDisc
    {
        protected override void BuildRenderTree(RenderTreeBuilder builder) { }
        protected override Task OnAfterRenderAsync(bool firstRender) => Task.CompletedTask;
    }

#pragma warning disable BL0006 // The test renderer attaches the component without rendering third-party controls.
    private sealed class TestRenderer(IServiceProvider services)
        : Renderer(services, services.GetRequiredService<ILoggerFactory>())
    {
        public override Dispatcher Dispatcher { get; } = Dispatcher.CreateDefault();
        public void Attach(IComponent component) => AssignRootComponentId(component);
        protected override void HandleException(Exception exception) => throw exception;
        protected override Task UpdateDisplayAsync(in RenderBatch renderBatch) => Task.CompletedTask;
    }
#pragma warning restore BL0006

    private sealed class TestNavigation : NavigationManager
    {
        public TestNavigation() => Initialize("https://localhost/", "https://localhost/contribution/Lyq/adddisc");
        protected override void NavigateToCore(string uri, bool forceLoad) => this.Uri = ToAbsoluteUri(uri).ToString();
    }
}