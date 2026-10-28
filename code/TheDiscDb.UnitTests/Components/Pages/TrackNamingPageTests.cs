using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Components.HtmlRendering.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Sqids;
using TheDiscDb.Client;
using TheDiscDb.Components.Pages;
using TheDiscDb.Data.Changes;
using TheDiscDb.Data.Changes.Track;
using TheDiscDb.InputModels;
using TheDiscDb.Services.EditSuggestions;
using TheDiscDb.Services.Server;
using TheDiscDb.UnitTests.Data.Changes;
using TheDiscDb.Web.Data;

namespace TheDiscDb.UnitTests.Components.Pages;

public class TrackNamingPageTests
{
    [Test]
    [Arguments("movie")]
    [Arguments("series")]
    [Arguments("boxset")]
    public async Task TitlePage_DisplaysCurrentNamesAndSectionEditLinks(string type)
    {
        await using var services = await CreateServicesAsync(type);
        var cache = services.GetRequiredService<CacheHelper>();
        await PrimeCacheAsync(cache, type);

        await using (var db = services.GetRequiredService<IDbContextFactory<SqlServerDataContext>>().CreateDbContext())
        {
            var track = await db.Set<Track>().SingleAsync(t => t.Type == "aUdIo");
            var original = TrackFieldsUpdate.SnapshotFrom(track,
                type == "boxset" ? null : ChangeTestSeed.MediaItemSlug,
                type == "boxset" ? ChangeTestSeed.MediaItemSlug : null,
                ChangeTestSeed.ReleaseSlug, ChangeTestSeed.DiscSlug, ChangeTestSeed.TitleIndex);
            var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
            var row = new TracksEdit.TrackEditRow(track, original) { EditDescription = "Approved commentary" };
            var suggestion = await services.GetRequiredService<IEditSuggestionService>().SubmitAsync(
                "test-user", EditSuggestionSource.Web, null,
                [new SubmitChangeInput(TrackFieldsUpdate.Key,
                    JsonSerializer.Serialize(row.BuildProposed(), options),
                    JsonSerializer.Serialize(original, options))], CancellationToken.None);

            var pendingHtml = await RenderAsync<TitleDetail>(services, Parameters(type));
            await Assert.That(pendingHtml).Contains("Original track desc");
            await Assert.That(pendingHtml).DoesNotContain("Approved commentary");

            var change = await services.GetRequiredService<IEditSuggestionReviewService>().ApproveChangeAsync(
                suggestion.Id, suggestion.Changes.Single().Id, "test-admin", null, CancellationToken.None);
            await Assert.That(change!.Status).IsEqualTo(EditSuggestionChangeStatus.Applied);
        }

        var html = await RenderAsync<TitleDetail>(services, Parameters(type));

        await Assert.That(html).Contains("Approved commentary");
        await Assert.That(html).DoesNotContain("Original track desc");
        await Assert.That(html).Contains("Foreign dialogue only");
        await Assert.That(html).Contains("DTS-HD MA (English)");
        await Assert.That(html).Contains("PGS (French)");
        await Assert.That(html).Contains("Suggest edits to audio track names");
        await Assert.That(html).Contains("Suggest edits to subtitles track names");
        await Assert.That(html.Split("title=\"Copy track name\"", StringSplitOptions.None).Length - 1).IsEqualTo(2);
        await Assert.That(html.Split("/tracks/edit", StringSplitOptions.None).Length - 1).IsEqualTo(2);
        var path = type == "boxset"
            ? "/boxset/the-movie/discs/disc-one/00001/mpls/tracks/edit"
            : $"/{type}/the-movie/releases/the-release-slug/discs/disc-one/00001/mpls/tracks/edit";
        await Assert.That(html).Contains(path);
    }

    [Test]
    [Arguments(null)]
    [Arguments("")]
    [Arguments("   ")]
    [Arguments("Director commentary")]
    public async Task TitlePage_CopyButtonsOnlyAppearForNonblankTrackNames(string? name)
    {
        await using var services = await CreateServicesAsync("movie");
        await using (var db = services.GetRequiredService<IDbContextFactory<SqlServerDataContext>>().CreateDbContext())
        {
            foreach (var track in await db.Set<Track>().ToListAsync())
            {
                track.Description = name;
            }
            await db.SaveChangesAsync();
        }

        var html = await RenderAsync<TitleDetail>(services, Parameters("movie"));
        var expectedCount = string.IsNullOrWhiteSpace(name) ? 0 : 3;

        await Assert.That(html.Split("title=\"Copy track name\"", StringSplitOptions.None).Length - 1).IsEqualTo(expectedCount);
    }

    [Test]
    [Arguments("movie")]
    [Arguments("boxset")]
    public async Task TitlePage_CombinedFilenameRouteProducesValidEditLink(string type)
    {
        await using var services = await CreateServicesAsync(type);
        var parameters = Parameters(type);
        parameters[nameof(TitleDetail.File)] = "00001.mpls";
        parameters[nameof(TitleDetail.Extension)] = null;

        var html = await RenderAsync<TitleDetail>(services, parameters);

        await Assert.That(html).Contains("/00001.mpls/tracks/edit");
        await Assert.That(html).DoesNotContain("/00001.mpls//tracks/edit");
    }

    [Test]
    [Arguments(false, true)]
    [Arguments(true, false)]
    public async Task TitlePage_UnavailableTrackEditingHasNoLinks(bool videoOnly, bool slugless)
    {
        await using var services = await CreateServicesAsync("movie", videoOnly, slugless);
        var parameters = Parameters("movie");
        if (slugless)
        {
            parameters[nameof(TitleDetail.SlugOrIndex)] = "0";
        }

        var html = await RenderAsync<TitleDetail>(services, parameters);

        await Assert.That(html).Contains("Video Tracks");
        await Assert.That(html).DoesNotContain("/tracks/edit");
    }

    [Test]
    [Arguments("movie")]
    [Arguments("series")]
    [Arguments("boxset")]
    public async Task Editor_ShowsAllSupportedTracksAndCurrentNames(string type)
    {
        await using var services = await CreateServicesAsync(type);
        await PrimeCacheAsync(services.GetRequiredService<CacheHelper>(), type);
        await using (var db = services.GetRequiredService<IDbContextFactory<SqlServerDataContext>>().CreateDbContext())
        {
            var track = await db.Set<Track>().SingleAsync(t => t.Type == "aUdIo");
            track.Description = "Latest approved name";
            await db.SaveChangesAsync();
        }

        var html = await RenderAsync<TracksEdit>(services, Parameters(type));

        await Assert.That(html).Contains("Latest approved name");
        await Assert.That(html).DoesNotContain("Original track desc");
        await Assert.That(html).Contains("Foreign dialogue only");
        await Assert.That(html).Contains("Audio track 2 name");
        await Assert.That(html).Contains("Subtitles track 8 name");
        await Assert.That(html).Contains("Subtitles track 9 name");
        await Assert.That(html).Contains("PGS");
        await Assert.That(html).Contains("(French)");
        await Assert.That(html).DoesNotContain("Video track");
        await Assert.That(html).Contains("Leave a name blank to remove it");
    }

    private static async Task<ServiceProvider> CreateServicesAsync(string type, bool videoOnly = false, bool slugless = false)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddMemoryCache();
        services.AddDbContextFactory<SqlServerDataContext>(options => options.UseInMemoryDatabase(Guid.NewGuid().ToString()));
        services.AddSingleton<CacheHelper>();
        services.AddSingleton<NavigationManager, TestNavigationManager>();
        services.AddSingleton<IClipboardService, TestClipboardService>();
        services.AddSingleton(new IdEncoder(new SqidsEncoder<int>()));
        services.AddIdentityCore<TheDiscDbUser>().AddEntityFrameworkStores<SqlServerDataContext>();
        services.AddSingleton<AuthenticationStateProvider, TestAuthenticationStateProvider>();
        services.AddSingleton<IChangeFactory>(new ChangeFactory([
            new ChangeBuilder<TrackFieldsDetails>(TrackFieldsUpdate.Key, (d, options) => new TrackFieldsUpdate(d, options))
        ]));
        services.AddScoped<IEditSuggestionHistoryService, EditSuggestionHistoryService>();
        services.AddScoped<IEditSuggestionService, EditSuggestionService>();
        services.AddScoped<IEditSuggestionReviewService, EditSuggestionReviewService>();
        var provider = services.BuildServiceProvider();

        await using var db = provider.GetRequiredService<IDbContextFactory<SqlServerDataContext>>().CreateDbContext();
        var seed = ChangeTestSeed.Seed(db);
        seed.MediaItem.Type = type;
        seed.Track.Type = "aUdIo";
        seed.Title.Item!.Chapters.Clear();
        seed.Title.Tracks.Add(new Track { Index = 0, Type = "Video", Name = "H.264" });
        seed.Title.Tracks.Add(new Track
        {
            Index = 8,
            Type = "Subtitle",
            Name = "PGS",
            Language = "French",
            Description = "Foreign dialogue only",
        });
        seed.Title.Tracks.Add(new Track { Index = 9, Type = "SUBTITLES", Name = "PGS", Language = "English" });
        if (videoOnly)
        {
            seed.Title.Tracks = seed.Title.Tracks.Where(t => t.Type == "Video").ToList();
        }
        if (slugless)
        {
            seed.ReleaseDisc.Slug = null;
        }
        if (type == "boxset")
        {
            db.BoxSets.Add(new Boxset { Slug = ChangeTestSeed.MediaItemSlug, Title = "Test Boxset", Release = seed.Release });
        }
        await db.SaveChangesAsync();
        return provider;
    }

    private static async Task PrimeCacheAsync(CacheHelper cache, string type)
    {
        if (type == "boxset")
        {
            await cache.GetBoxsetAsync(ChangeTestSeed.MediaItemSlug);
        }
        else
        {
            await cache.GetMediaItemDetail(type, ChangeTestSeed.MediaItemSlug);
        }
    }

    private static Dictionary<string, object?> Parameters(string type) => new()
    {
        [nameof(TitleDetail.Type)] = type,
        [nameof(TitleDetail.Slug)] = ChangeTestSeed.MediaItemSlug,
        [nameof(TitleDetail.ReleaseSlug)] = type == "boxset" ? null : ChangeTestSeed.ReleaseSlug,
        [nameof(TitleDetail.SlugOrIndex)] = ChangeTestSeed.DiscSlug,
        [nameof(TitleDetail.File)] = "00001",
        [nameof(TitleDetail.Extension)] = "mpls",
    };

    private static async Task<string> RenderAsync<TComponent>(IServiceProvider services, Dictionary<string, object?> parameters)
        where TComponent : IComponent
    {
        await using var renderer = new TestHtmlRenderer(services, services.GetRequiredService<ILoggerFactory>());
        return await renderer.Dispatcher.InvokeAsync(async () =>
        {
            var result = renderer.BeginRenderingComponent(typeof(TComponent), ParameterView.FromDictionary(parameters));
            await result.QuiescenceTask;
            return result.ToHtmlString();
        });
    }

    private sealed class TestHtmlRenderer(IServiceProvider services, ILoggerFactory loggerFactory)
        : StaticHtmlRenderer(services, loggerFactory)
    {
        protected override IComponent ResolveComponentForRenderMode(
            Type componentType, int? parentComponentId, IComponentActivator componentActivator, IComponentRenderMode renderMode)
            => componentActivator.CreateInstance(componentType);
    }

    private sealed class TestNavigationManager : NavigationManager
    {
        public TestNavigationManager() => Initialize("https://localhost/", "https://localhost/");
    }

    private sealed class TestAuthenticationStateProvider : AuthenticationStateProvider
    {
        public override Task<AuthenticationState> GetAuthenticationStateAsync() =>
            Task.FromResult(new AuthenticationState(new ClaimsPrincipal(new ClaimsIdentity())));
    }

    private sealed class TestClipboardService : IClipboardService
    {
        public ValueTask<string> ReadTextAsync(CancellationToken cancellationToken = default) => ValueTask.FromResult("");
        public ValueTask WriteTextAsync(string text, CancellationToken cancellationToken = default) => ValueTask.CompletedTask;
    }
}