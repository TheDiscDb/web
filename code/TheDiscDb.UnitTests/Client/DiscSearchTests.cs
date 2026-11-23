using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using TheDiscDb.Client;
using TheDiscDb.Client.Controls;
using TheDiscDb.Client.Pages.Contribute;
using TheDiscDb.Client.Services;
using TheDiscDb.OpticalDiscManifest.Generation;

namespace TheDiscDb.UnitTests.Client;

public class DiscSearchTests
{
    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task SearchSession_QueryHash_SearchesAndRetriesWithoutDiscSelection(bool found)
    {
        using var fixture = new Fixture(_ => Data(found ? [Media("movie", "Movie", [Release("one")])] : [], []));
        await using var session = new DiscSearchSession(fixture.Search, null!);

        await session.SearchHashAsync("  abcdef  ", CancellationToken.None);

        await Assert.That(session.Result!.Hash).IsEqualTo("ABCDEF");
        await Assert.That(session.Result.Found).IsEqualTo(found);
        await Assert.That(session.CanRetry).IsTrue();
        await Assert.That(session.CanPrepareContribution).IsFalse();
        await Assert.That(async () => { await session.PrepareContributionAsync(
            found ? session.Result.Titles.Single() : null, "https://localhost/", _ => { }, CancellationToken.None); })
            .Throws<InvalidOperationException>();

        await session.RetryAsync(CancellationToken.None);

        await Assert.That(fixture.Requests.Select(request => request.Hash)).IsEquivalentTo(["ABCDEF", "ABCDEF"]);
    }

    [Test]
    public async Task SearchSession_QueryHashFailure_CanRetryWithoutSelectingFolder()
    {
        using var fixture = new Fixture(_ => throw new HttpRequestException("Offline"));
        await using var session = new DiscSearchSession(fixture.Search, null!);

        await Assert.That(() => session.SearchHashAsync("ABC", CancellationToken.None)).Throws<HttpRequestException>();
        await Assert.That(session.Result).IsNull();
        await Assert.That(session.CanRetry).IsTrue();
        fixture.Handler.Respond = _ => Data([], []);

        await session.RetryAsync(CancellationToken.None);

        await Assert.That(session.Result!.Hash).IsEqualTo("ABC");
        await Assert.That(session.Result.Found).IsFalse();
    }

    [Test]
    public async Task SearchSession_QueryHash_ReleasesSelectedDisc_AndClearRemovesRetryState()
    {
        using var fixture = new Fixture(_ => Data([], []));
        await using var session = new DiscSearchSession(fixture.Search, null!);
        int disposed = 0;
        await session.SearchAsync(Selection(() => disposed++), CancellationToken.None);
        await Assert.That(session.CanPrepareContribution).IsTrue();

        await session.SearchHashAsync("ABC", CancellationToken.None);

        await Assert.That(disposed).IsEqualTo(1);
        await Assert.That(session.CanPrepareContribution).IsFalse();
        await session.ClearAsync();
        await Assert.That(session.Result).IsNull();
        await Assert.That(session.CanRetry).IsFalse();

        await session.SearchAsync(Selection(), CancellationToken.None);

        await Assert.That(session.CanPrepareContribution).IsTrue();
        await Assert.That(session.Result!.Hash).IsNotEqualTo("ABC");
    }

    [Test]
    public async Task SearchSession_StaleQueryHashCannotReplaceNewQuery()
    {
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var resume = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var fixture = new Fixture(_ => Data([], []));
        fixture.Handler.BeforeResponse = async (request, token) =>
        {
            if (request.Hash == "OLD")
            {
                started.SetResult();
                await resume.Task.WaitAsync(token);
            }
        };
        await using var session = new DiscSearchSession(fixture.Search, null!);
        Task oldSearch = session.SearchHashAsync("OLD", CancellationToken.None);
        await started.Task.WaitAsync(TimeSpan.FromSeconds(10));
        try
        {
            await session.SearchHashAsync("NEW", CancellationToken.None);
            resume.SetResult();
            await oldSearch;

            await Assert.That(session.Result!.Hash).IsEqualTo("NEW");
        }
        finally
        {
            resume.TrySetResult();
            await oldSearch;
        }
    }

    [Test]
    public async Task SearchAsync_UsesReleaseFrontCoverAndBoxsetCover_AndAllowsMissingCovers()
    {
        using var fixture = new Fixture(_ => Data(
            [Media("movie", "Movie", [Release("covered", "Movie/front.jpg"), Release("missing")])],
            [Boxset("collection", "Boxsets/collection/front.jpg"), Boxset("missing-cover")]));

        var result = await fixture.Search.SearchAsync("ABC", CancellationToken.None);

        await Assert.That(result.Titles.Single().Releases.Single(release => release.Title == "covered").ImageUrl)
            .IsEqualTo("Movie/front.jpg");
        await Assert.That(result.Titles.Single().Releases.Single(release => release.Title == "missing").ImageUrl).IsNull();
        await Assert.That(result.Boxsets.Single(release => release.Url == "/boxset/collection").ImageUrl)
            .IsEqualTo("Boxsets/collection/front.jpg");
        await Assert.That(result.Boxsets.Single(release => release.Url == "/boxset/missing-cover").ImageUrl).IsNull();

        using var services = new ServiceCollection().AddLogging().BuildServiceProvider();
        await using var renderer = new HtmlRenderer(services, services.GetRequiredService<ILoggerFactory>());
        string html = await renderer.Dispatcher.InvokeAsync(async () =>
        {
            var rendered = await renderer.RenderComponentAsync<DiscSearchResults>(ParameterView.FromDictionary(
                new Dictionary<string, object?> { [nameof(DiscSearchResults.Result)] = result }));
            return rendered.ToHtmlString();
        });
        await Assert.That(html).Contains("/images/Movie/front.jpg?width=104&amp;height=148");
        await Assert.That(html).Contains("/images/Boxsets/collection/front.jpg?width=104&amp;height=148");
        await Assert.That(html).Contains("covered front cover");
        await Assert.That(html.Split("<img", StringSplitOptions.None).Length - 1).IsEqualTo(2);
    }

    [Test]
    public async Task SearchAsync_MapsEveryReleaseAndBoxset_WithNaturalKeyLinks()
    {
        using var fixture = new Fixture(_ => Data(
            [Media("a movie", "Movie", [Release("edition-one"), Release("edition-two")]),
             Media("a-series", "Series", [Release("season-one")])],
            [Boxset("a-boxset")]));

        var result = await fixture.Search.SearchAsync("abcdef", CancellationToken.None);

        await Assert.That(result.Found).IsTrue();
        await Assert.That(fixture.Requests.Single().Hash).IsEqualTo("ABCDEF");
        await Assert.That(result.Titles.Count).IsEqualTo(2);
        await Assert.That(result.Titles.SelectMany(title => title.Releases).Select(release => release.Url))
            .IsEquivalentTo(["/movie/a%20movie/releases/edition-one", "/movie/a%20movie/releases/edition-two",
                "/series/a-series/releases/season-one"]);
        await Assert.That(result.Boxsets.Single().Url).IsEqualTo("/boxset/a-boxset");
        await Assert.That(result.Titles[0].Releases[0].Discs.Single()).IsEqualTo("Feature - Blu-ray");
    }

    [Test]
    public async Task SearchAsync_PagesConnectionsIndependently_AndDeduplicatesByRoute()
    {
        using var fixture = new Fixture(request => request.MediaAfter is null
            ? Data([Media("movie", "Movie", [Release("one")])], [Boxset("collection")],
                mediaMore: true, mediaCursor: "media-2")
            : Data([Media("movie", "Movie", [Release("one"), Release("two")])], [],
                includeBoxsets: false));

        var result = await fixture.Search.SearchAsync("ABC", CancellationToken.None);

        await Assert.That(fixture.Requests.Count).IsEqualTo(2);
        await Assert.That(fixture.Requests[1].MediaAfter).IsEqualTo("media-2");
        await Assert.That(fixture.Requests[1].IncludeMedia).IsTrue();
        await Assert.That(fixture.Requests[1].IncludeBoxsets).IsFalse();
        await Assert.That(result.Titles.Single().Releases.Count).IsEqualTo(2);
        await Assert.That(result.Boxsets.Count).IsEqualTo(1);
    }

    [Test]
    public async Task SearchAsync_BoxsetPagination_DoesNotRepeatFinishedMediaQuery()
    {
        using var fixture = new Fixture(request => request.BoxsetAfter is null
            ? Data([], [Boxset("one")], boxsetMore: true, boxsetCursor: "boxset-2")
            : Data([], [Boxset("one"), Boxset("two")], includeMedia: false));

        var result = await fixture.Search.SearchAsync("ABC", CancellationToken.None);

        await Assert.That(fixture.Requests[1].IncludeMedia).IsFalse();
        await Assert.That(fixture.Requests[1].IncludeBoxsets).IsTrue();
        await Assert.That(result.Titles).IsEmpty();
        await Assert.That(result.Boxsets.Count).IsEqualTo(2);
        await Assert.That(result.Found).IsTrue();
    }

    [Test]
    [Arguments(0)]
    [Arguments(1)]
    [Arguments(2)]
    public async Task SearchAsync_PartialFailureOrInvalidPagination_IsNotNoMatch(int failure)
    {
        using var fixture = new Fixture(_ => failure switch
        {
            0 => new { mediaItems = Connection("MediaItemsConnection", []), boxsets = (object?)null },
            1 => Data([], [], boxsetMore: true),
            _ => Data([], [], mediaMore: true, mediaCursor: "repeated")
        });

        await Assert.That(async () => { await fixture.Search.SearchAsync("ABC", CancellationToken.None); })
            .Throws<InvalidOperationException>();
        await Assert.That(fixture.Requests.Count).IsLessThanOrEqualTo(2);
    }

    [Test]
    public async Task SearchAsync_LaterPageNetworkFailure_DoesNotReturnPartialResults()
    {
        using var fixture = new Fixture(request => request.MediaAfter is null
            ? Data([Media("movie", "Movie", [Release("one")])], [], mediaMore: true, mediaCursor: "next")
            : throw new HttpRequestException("Database unavailable"));

        await Assert.That(async () => { await fixture.Search.SearchAsync("ABC", CancellationToken.None); })
            .Throws<HttpRequestException>();
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task SearchSession_HashOnly_DoesNotReadVideoOrPrepareDraft(bool found)
    {
        using var fixture = new Fixture(_ => Data(found ? [Media("movie", "Movie", [Release("one")])] : [], []));
        // No uploader is available: searching must not try to generate, save, or upload a manifest.
        await using var session = new DiscSearchSession(fixture.Search, null!);

        await session.SearchAsync(Selection(), CancellationToken.None);

        await Assert.That(session.Result!.Found).IsEqualTo(found);
        await Assert.That(session.CanRetry).IsTrue();
        await Assert.That(fixture.Requests.Count).IsEqualTo(1);
    }

    [Test]
    public async Task SearchSession_KnownTitle_SavesLocalReferenceOnlyAfterAdd()
    {
        using var fixture = new Fixture(_ => Data([Media("movie", "Movie", [Release("one")])], []));
        await using var storage = new BrowserScanStorage(new BrowserScanFlowTests.MemoryJsRuntime());
        // A null manifest scanner proves the known-title path only saves a reference.
        var uploader = new DiscScanUploader(null!, null!, new HttpClient(new NoUploadHandler()), storage);
        await using var session = new DiscSearchSession(fixture.Search, uploader);
        await session.SearchAsync(Selection(), CancellationToken.None);
        string hash = session.Result!.Hash;
        await Assert.That(await storage.GetScanAsync(hash)).IsNull();

        var prepared = await session.PrepareContributionAsync(
            session.Result.Titles.Single(), "https://localhost/", _ => { }, CancellationToken.None);

        var saved = await storage.GetScanAsync(hash);
        await Assert.That(prepared.Title!.ExternalId).IsEqualTo("603");
        await Assert.That(saved!.Draft.IsExistingDiscReference).IsTrue();
        await Assert.That(saved.Draft.ManifestJson).IsEmpty();
        await Assert.That(fixture.Requests.Count).IsEqualTo(2);
    }

    [Test]
    [Arguments(0)]
    [Arguments(1)]
    [Arguments(2)]
    public async Task SearchSession_WithoutReusableTitle_SubmitCreatesFullLocalScan(int source)
    {
        using var fixture = new Fixture(_ => Data(
            source == 2 ? [Media("movie", "Movie", [Release("one")], tmdb: null)] : [],
            source == 1 ? [Boxset("collection")] : []));
        await using var storage = new BrowserScanStorage(new BrowserScanFlowTests.MemoryJsRuntime());
        var uploader = new DiscScanUploader(null!,
            new BrowserOpticalDiscManifestScanner(new OpticalDiscManifestGenerator()),
            new HttpClient(new NoUploadHandler()), storage);
        await using var session = new DiscSearchSession(fixture.Search, uploader);
        await session.SearchAsync(Selection(), CancellationToken.None);
        await Assert.That(session.Result!.Found).IsEqualTo(source != 0);
        await Assert.That(session.Result.Titles.Any(title => title.CanContribute)).IsFalse();
        await Assert.That(await storage.GetScanAsync(session.Result.Hash)).IsNull();

        var prepared = await session.PrepareContributionAsync(
            null, "https://localhost/", _ => { }, CancellationToken.None);

        var saved = await storage.GetScanAsync(prepared.Hash);
        await Assert.That(prepared.Title).IsNull();
        await Assert.That(saved!.Draft.IsExistingDiscReference).IsFalse();
        await Assert.That(saved.Draft.ManifestJson).IsNotEmpty();
        await Assert.That(fixture.Requests.Count).IsEqualTo(1);
    }

    [Test]
    public async Task SearchSession_DisappearingMatch_DoesNotSaveReferenceOrSilentlyScan()
    {
        using var fixture = new Fixture(_ => Data([Media("movie", "Movie", [Release("one")])], []));
        await using var session = new DiscSearchSession(fixture.Search, null!);
        await session.SearchAsync(Selection(), CancellationToken.None);
        fixture.Handler.Respond = _ => Data([], []);

        await Assert.That(async () => { await session.PrepareContributionAsync(
            session.Result!.Titles.Single(), "https://localhost/", _ => { }, CancellationToken.None); })
            .Throws<InvalidOperationException>();
        await Assert.That(fixture.Requests.Count).IsEqualTo(2);
    }

    [Test]
    public async Task SearchSession_StorageFailure_IsAnExplicitPreparationFailure()
    {
        using var fixture = new Fixture(_ => Data([Media("movie", "Movie", [Release("one")])], []));
        await using var storage = new BrowserScanStorage(new BrowserScanFlowTests.MemoryJsRuntime { FailWrites = true });
        var uploader = new DiscScanUploader(null!, null!, new HttpClient(new NoUploadHandler()), storage);
        await using var session = new DiscSearchSession(fixture.Search, uploader);
        await session.SearchAsync(Selection(), CancellationToken.None);

        await Assert.That(async () => { await session.PrepareContributionAsync(
            session.Result!.Titles.Single(), "https://localhost/", _ => { }, CancellationToken.None); })
            .Throws<InvalidOperationException>();
        await Assert.That(await storage.GetScanAsync(session.Result!.Hash)).IsNull();
    }

    [Test]
    public async Task SearchSession_StaleLookupCannotReplaceNewDisc_AndSelectionsAreReleased()
    {
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var resume = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var fixture = new Fixture(_ => Data([], []));
        fixture.Handler.BeforeResponse = async (request, token) =>
        {
            if (fixture.Requests.Count == 1)
            {
                started.SetResult();
                await resume.Task.WaitAsync(token);
            }
        };
        int disposed = 0;
        await using var session = new DiscSearchSession(fixture.Search, null!);
        Task oldSearch = session.SearchAsync(Selection(() => disposed++), CancellationToken.None);
        await started.Task.WaitAsync(TimeSpan.FromSeconds(10));
        try
        {
            await session.SearchAsync(Selection(() => disposed++, 2048), CancellationToken.None);
            string newHash = session.Result!.Hash;
            resume.SetResult();
            await oldSearch;

            await Assert.That(session.Result.Hash).IsEqualTo(newHash);
            await Assert.That(disposed).IsEqualTo(1);
            await session.DisposeAsync();
            await Assert.That(disposed).IsEqualTo(2);
        }
        finally
        {
            resume.TrySetResult();
            await oldSearch;
        }
    }

    [Test]
    public async Task SearchSession_RetryFailureClearsOldResults_AndCanRetryWithoutReselecting()
    {
        using var fixture = new Fixture(_ => Data([], []));
        await using var session = new DiscSearchSession(fixture.Search, null!);
        await session.SearchAsync(Selection(), CancellationToken.None);
        fixture.Handler.Respond = _ => throw new HttpRequestException("Offline");

        await Assert.That(() => session.RetryAsync(CancellationToken.None)).Throws<HttpRequestException>();
        await Assert.That(session.Result).IsNull();
        await Assert.That(session.CanRetry).IsTrue();
        fixture.Handler.Respond = _ => Data([], [Boxset("collection")]);
        await session.RetryAsync(CancellationToken.None);
        await Assert.That(session.Result!.Found).IsTrue();
    }

    [Test]
    public async Task SearchSession_CancelledLookup_DoesNotBecomeNoMatch()
    {
        using var cancellation = new CancellationTokenSource();
        using var fixture = new Fixture(_ => Data([], []));
        fixture.Handler.BeforeResponse = (_, _) =>
        {
            cancellation.Cancel();
            return Task.CompletedTask;
        };
        await using var session = new DiscSearchSession(fixture.Search, null!);

        await Assert.That(() => session.SearchAsync(Selection(), cancellation.Token))
            .Throws<OperationCanceledException>();
        await Assert.That(session.Result).IsNull();
    }

    private static DiscFileSelection Selection(Action? dispose = null, long size = 1024)
        => new([new DiscScanFile("BDMV/STREAM/00001.m2ts", "00001.m2ts", size, DateTime.UnixEpoch,
            (_, _) => throw new InvalidOperationException("Video payload must not be read."))],
            () => { dispose?.Invoke(); return ValueTask.CompletedTask; });

    private static object Media(string slug, string type, object[] releases, string? tmdb = "603")
        => new { __typename = "MediaItem", id = slug.GetHashCode(StringComparison.Ordinal), title = slug, year = 2020, slug, type,
            externalids = new { __typename = "ExternalIds", id = slug.GetHashCode(StringComparison.Ordinal), tmdb }, releases };

    private static object Release(string slug, string? imageUrl = null)
        => new { __typename = "Release", id = slug.GetHashCode(StringComparison.Ordinal), title = slug, year = 2024, slug, upc = "123456789012", imageUrl, discs = Discs() };

    private static object Boxset(string slug, string? imageUrl = null)
        => new { __typename = "Boxset", id = slug.GetHashCode(StringComparison.Ordinal), title = slug, slug, imageUrl, release = Release("edition", "Boxsets/release-front.jpg") };

    private static object[] Discs()
        => [new { __typename = "ReleaseDisc", id = 1, index = 1, name = "Feature", format = "Blu-ray" }];

    private static object Connection(string type, object[] nodes, bool more = false, string? cursor = null)
        => new { __typename = type, nodes,
            pageInfo = new { __typename = "PageInfo", hasNextPage = more, endCursor = cursor } };

    private static object Data(object[] media, object[] boxsets, bool mediaMore = false, string? mediaCursor = null,
        bool boxsetMore = false, string? boxsetCursor = null, bool includeMedia = true, bool includeBoxsets = true)
    {
        var data = new Dictionary<string, object>();
        if (includeMedia)
        {
            data["mediaItems"] = Connection("MediaItemsConnection", media, mediaMore, mediaCursor);
        }
        if (includeBoxsets)
        {
            data["boxsets"] = Connection("BoxsetsConnection", boxsets, boxsetMore, boxsetCursor);
        }
        return data;
    }

    private sealed record SearchRequest(string Hash, string? MediaAfter, string? BoxsetAfter, bool IncludeMedia, bool IncludeBoxsets);

    private sealed class Fixture : IDisposable
    {
        private readonly ServiceProvider services;
        public SearchHandler Handler { get; }
        public List<SearchRequest> Requests => this.Handler.Requests;
        public DiscSearchService Search { get; }

        public Fixture(Func<SearchRequest, object> respond)
        {
            this.Handler = new SearchHandler { Respond = respond };
            var services = new ServiceCollection();
            services.AddTheDiscDbClient().ConfigureHttpClient(
                client => client.BaseAddress = new Uri("https://localhost/graphql"),
                builder => builder.ConfigurePrimaryHttpMessageHandler(() => this.Handler));
            this.services = services.BuildServiceProvider();
            this.Search = new DiscSearchService(this.services.GetRequiredService<SearchDiscReleasesQuery>());
        }

        public void Dispose()
        {
            this.services.Dispose();
            this.Handler.Dispose();
        }
    }

    private sealed class SearchHandler : HttpMessageHandler
    {
        public required Func<SearchRequest, object> Respond { get; set; }
        public Func<SearchRequest, CancellationToken, Task>? BeforeResponse { get; set; }
        public List<SearchRequest> Requests { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage message, CancellationToken cancellationToken)
        {
            using var json = JsonDocument.Parse(await message.Content!.ReadAsStringAsync(cancellationToken));
            var variables = json.RootElement.GetProperty("variables");
            string? Cursor(string name) => variables.TryGetProperty(name, out var value) ? value.GetString() : null;
            var request = new SearchRequest(variables.GetProperty("hash").GetString()!,
                Cursor("mediaAfter"), Cursor("boxsetAfter"),
                variables.GetProperty("includeMedia").GetBoolean(), variables.GetProperty("includeBoxsets").GetBoolean());
            this.Requests.Add(request);
            if (this.BeforeResponse is not null)
            {
                await this.BeforeResponse(request, cancellationToken);
            }
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(JsonSerializer.Serialize(new { data = this.Respond(request) }), Encoding.UTF8, "application/json")
            };
        }
    }

    private sealed class NoUploadHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => throw new InvalidOperationException("Searching and preparing a local draft must not upload anything.");
    }
}
