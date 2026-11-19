using System.Net;
using System.Reflection;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.JSInterop;
using TheDiscDb.Client;
using TheDiscDb.Client.Contributions;
using TheDiscDb.Client.Pages.Contribute;
using TheDiscDb.Client.Services;
using TheDiscDb.Core.DiscHash;
using TheDiscDb.InputModels;

namespace TheDiscDb.UnitTests.Client;

public class ReleaseDetailInputTests
{
    [Test]
    [Arguments("link")]
    [Arguments("remove")]
    public async Task HandleValidSubmit_ScanStateChangesDuringAwait_UsesSubmittedScan(string stage)
    {
        using var handler = new SubmissionHandler();
        using var services = CreateServices(handler);
        var js = new DraftJsRuntime();
        await using var storage = new ContributionDraftStorage(js);
        using var component = CreateComponent(services, handler, storage);
        void ClearScan()
        {
            component.DiscHash = null;
            SetProperty(component, "DiscScanDraft", null);
        }

        if (stage == "link")
        {
            handler.OnLink = ClearScan;
        }
        else
        {
            js.OnRemove = ClearScan;
        }

        await SubmitAsync(component);

        await Assert.That(new Uri(component.NavigationManager.Uri).AbsolutePath)
            .IsEqualTo("/contribution/contribution/discs/disc/identify");
        await Assert.That(handler.DiscsCreated).IsEqualTo(1);
        await Assert.That(handler.Attached).IsTrue();
    }

    [Test]
    public async Task HandleValidSubmit_AlreadySubmitting_CreatesOnlyOneContributionAndDisc()
    {
        using var handler = new SubmissionHandler { PauseCreation = true };
        using var services = CreateServices(handler);
        await using var storage = new ContributionDraftStorage(new DraftJsRuntime());
        using var component = CreateComponent(services, handler, storage);

        var firstSubmission = SubmitAsync(component);
        await handler.CreationStarted.Task.WaitAsync(TimeSpan.FromSeconds(10));
        var secondSubmission = SubmitAsync(component);
        handler.ResumeCreation.SetResult();
        await Task.WhenAll(firstSubmission, secondSubmission);

        await Assert.That(handler.ContributionsCreated).IsEqualTo(1);
        await Assert.That(handler.DiscsCreated).IsEqualTo(1);
    }

    [Test]
    public async Task HandleValidSubmit_InvalidReleaseDate_AllowsRetry()
    {
        using var handler = new SubmissionHandler();
        using var services = CreateServices(handler);
        await using var storage = new ContributionDraftStorage(new DraftJsRuntime());
        using var component = CreateComponent(services, handler, storage);
        SetField(component, "releaseDate", "invalid");

        await SubmitAsync(component);
        await Assert.That(handler.ContributionsCreated).IsEqualTo(0);

        SetField(component, "releaseDate", "2024-01-01");
        await SubmitAsync(component);

        await Assert.That(handler.ContributionsCreated).IsEqualTo(1);
        await Assert.That(handler.Attached).IsTrue();
    }

    private static ServiceProvider CreateServices(SubmissionHandler handler)
    {
        var services = new ServiceCollection();
        services.AddTheDiscDbClient()
            .ConfigureHttpClient(
                client => client.BaseAddress = new Uri("https://localhost/graphql"),
                builder => builder.ConfigurePrimaryHttpMessageHandler(() => handler));
        services.AddContributionClient()
            .ConfigureHttpClient(
                client => client.BaseAddress = new Uri("https://localhost/graphql/contributions"),
                builder => builder.ConfigurePrimaryHttpMessageHandler(() => handler));
        return services.BuildServiceProvider();
    }

    private static ReleaseDetailInput CreateComponent(
        ServiceProvider services,
        SubmissionHandler handler,
        ContributionDraftStorage storage)
    {
        var component = new ReleaseDetailInput
        {
            ContributionClient = services.GetRequiredService<IContributionClient>(),
            HttpClient = new HttpClient(handler) { BaseAddress = new Uri("https://localhost/") },
            NavigationManager = new TestNavigation(),
            DiscHash = "abc"
        };
        SetProperty(component, "DraftStorage", storage);
        SetProperty(component, "ContentHashQuery", services.GetRequiredService<GetDiscDetailByContentHashQuery>());
        SetProperty(component, "DiscScanDraft", new DiscScanDraft
        {
            DiscHash = "abc",
            Format = DiscFormatConstants.BluRay,
            HasManualHashOverride = true
        });
        var request = (ContributionMutationRequestInput)typeof(ReleaseDetailInput)
            .GetField("request", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(component)!;
        request.MediaType = "movie";
        request.ExternalId = "603";
        request.ExternalProvider = "TMDB";
        request.Title = "The Matrix";
        request.Year = "1999";
        request.Asin = "B000000000";
        request.Upc = "012345678905";
        request.FrontImageUrl = "front.jpg";
        request.ReleaseTitle = "2024 Blu-ray";
        request.ReleaseSlug = "2024-blu-ray";
        SetField(component, "form", new ReleaseDetailFormBindings(request));
        SetField(component, "releaseDate", "2024-01-01");
        return component;
    }

    private static Task SubmitAsync(ReleaseDetailInput component)
        => (Task)typeof(ReleaseDetailInput)
            .GetMethod("HandleValidSubmit", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(component, null)!;

    private static void SetProperty(ReleaseDetailInput component, string name, object? value)
        => typeof(ReleaseDetailInput).GetProperty(name, BindingFlags.Instance | BindingFlags.NonPublic)!
            .SetValue(component, value);

    private static void SetField(ReleaseDetailInput component, string name, object value)
        => typeof(ReleaseDetailInput).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!
            .SetValue(component, value);

    private sealed class SubmissionHandler : HttpMessageHandler
    {
        public Action? OnLink { get; set; }
        public bool PauseCreation { get; init; }
        public TaskCompletionSource CreationStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource ResumeCreation { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public int ContributionsCreated { get; private set; }
        public int DiscsCreated { get; private set; }
        public bool Attached { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (request.RequestUri!.AbsolutePath.StartsWith("/api/", StringComparison.Ordinal))
            {
                if (request.RequestUri.AbsolutePath.EndsWith("/attach", StringComparison.Ordinal))
                {
                    this.Attached = true;
                }
                else
                {
                    this.OnLink?.Invoke();
                }

                return new HttpResponseMessage(HttpStatusCode.OK);
            }

            using var body = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(cancellationToken));
            string operation = body.RootElement.GetProperty("operationName").GetString()!;
            string data;
            switch (operation)
            {
                case "CreateContribution":
                    this.ContributionsCreated++;
                    this.CreationStarted.TrySetResult();
                    if (this.PauseCreation)
                    {
                        await this.ResumeCreation.Task.WaitAsync(cancellationToken);
                    }

                    data = """{"createContribution":{"__typename":"CreateContributionPayload","userContribution":{"__typename":"UserContribution","id":1,"encodedId":"contribution"},"errors":null}}""";
                    break;
                case "GetDiscDetailByContentHash":
                    data = """{"mediaItems":{"__typename":"MediaItemsConnection","nodes":[]}}""";
                    break;
                case "CreateDisc":
                    this.DiscsCreated++;
                    data = """{"createDisc":{"__typename":"CreateDiscPayload","userContributionDisc":{"__typename":"UserContributionDisc","id":1,"encodedId":"disc"},"errors":null}}""";
                    break;
                default:
                    throw new InvalidOperationException($"Unexpected GraphQL operation: {operation}");
            }

            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent($$"""{"data":{{data}}}""", Encoding.UTF8, "application/json")
            };
        }
    }

    private sealed class DraftJsRuntime : IJSRuntime, IJSObjectReference
    {
        public Action? OnRemove { get; set; }

        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, object?[]? args)
            => InvokeAsync<TValue>(identifier, CancellationToken.None, args);

        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, CancellationToken cancellationToken, object?[]? args)
        {
            if (identifier == "remove")
            {
                this.OnRemove?.Invoke();
            }

            return ValueTask.FromResult(identifier == "import" ? (TValue)(object)this : default!);
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class TestNavigation : NavigationManager
    {
        public TestNavigation() => Initialize("https://localhost/", "https://localhost/contribute/movie/603");

        protected override void NavigateToCore(string uri, bool forceLoad)
            => this.Uri = ToAbsoluteUri(uri).ToString();
    }
}
