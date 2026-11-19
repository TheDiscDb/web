using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.JSInterop;
using TheDiscDb.Client.Services;
using TheDiscDb.Core.DiscHash;

namespace TheDiscDb.UnitTests.Client;

public class BrowserScanFlowTests
{
    [Test]
    public async Task ContinueAsync_Anonymous_PersistsChoiceBeforeFullPageLogin()
    {
        var js = new MemoryJsRuntime();
        await using var storage = new BrowserScanStorage(js);
        await storage.SaveScanAsync(new DiscScanDraft { DiscHash = "ABC", ManifestJson = "scan" });
        var navigation = new TestNavigation();
        var service = new ContributionContinuationService(storage, new TestAuthentication(false), navigation);

        await service.ContinueAsync(new ContributionContinuation("series", "123", "ABC", "box", true), CancellationToken.None);

        var login = new Uri(navigation.Uri);
        var resume = QueryHelpers.ParseQuery(login.Query)["ReturnUrl"].ToString();
        string id = QueryHelpers.ParseQuery(new Uri(new Uri("https://localhost"), resume).Query)["draft"].ToString();
        var saved = await storage.GetContinuationAsync(id);
        await Assert.That(login.AbsolutePath).IsEqualTo("/Account/Login");
        await Assert.That(navigation.ForceLoad).IsTrue();
        await Assert.That(saved!.MediaType).IsEqualTo("series");
        await Assert.That(saved.ExternalId).IsEqualTo("123");
        await Assert.That(saved.BoxsetId).IsEqualTo("box");
        await Assert.That(saved.FromMatches).IsTrue();
        await Assert.That(saved.LocalScanId).IsEqualTo((await storage.GetScanAsync("abc"))!.Id);
        await Assert.That(resume.Contains("scan")).IsFalse();
    }

    [Test]
    public async Task ContinueAsync_Authenticated_SkipsLogin()
    {
        await using var storage = new BrowserScanStorage(new MemoryJsRuntime());
        var navigation = new TestNavigation();
        var service = new ContributionContinuationService(storage, new TestAuthentication(true), navigation);

        await service.ContinueAsync(new ContributionContinuation("movie", "603"), CancellationToken.None);

        await Assert.That(new Uri(navigation.Uri).AbsolutePath).IsEqualTo("/contribute/resume");
        await Assert.That(navigation.ForceLoad).IsFalse();
    }

    [Test]
    public async Task ContinueAsync_StorageFailure_DoesNotLeaveThePage()
    {
        var js = new MemoryJsRuntime { FailWrites = true };
        await using var storage = new BrowserScanStorage(js);
        var navigation = new TestNavigation();
        var service = new ContributionContinuationService(storage, new TestAuthentication(false), navigation);

        await Assert.That(() => service.ContinueAsync(new ContributionContinuation("movie", "603"), CancellationToken.None))
            .Throws<JSException>();
        await Assert.That(navigation.Uri).IsEqualTo("https://localhost/contribute");
    }

    [Test]
    public async Task RemoveScanAsync_OldContinuation_DoesNotDeleteANewerScan()
    {
        await using var storage = new BrowserScanStorage(new MemoryJsRuntime());
        await storage.SaveScanAsync(new DiscScanDraft { DiscHash = "ABC", ManifestJson = "old" });
        var old = await storage.GetScanAsync("abc");
        await storage.SaveScanAsync(new DiscScanDraft { DiscHash = "ABC", ManifestJson = "new" });

        await storage.RemoveScanAsync("abc", old!.Id);
        await Assert.That((await storage.GetScanAsync("ABC"))!.Draft.ManifestJson).IsEqualTo("new");
    }

    [Test]
    public async Task DetailsUrl_PreservesEscapedContextAndKnownDiscSource()
    {
        var choice = new ContributionContinuation("series", "42", "test/hash +", "AbC", true);
        var uri = new Uri(new Uri("https://localhost"), choice.DetailsUrl);
        var query = QueryHelpers.ParseQuery(uri.Query);
        await Assert.That(uri.AbsolutePath).IsEqualTo("/contribute/series/42");
        await Assert.That(query["discHash"].ToString()).IsEqualTo("test/hash +");
        await Assert.That(query["boxsetId"].ToString()).IsEqualTo("AbC");
        await Assert.That(query["fromMatches"].ToString()).IsEqualTo("true");
        string route = ContributionContinuation.MatchesUrl("test/hash +");
        var encoded = route.Split('/')[3];
        await Assert.That(DiscScanDraft.TryDecodeHashFromRoute(encoded, out string decoded)).IsTrue();
        await Assert.That(decoded).IsEqualTo("test/hash +");
    }

    [Test]
    public async Task MatchesTitle_SameHashUnderOtherTitle_IsNotAReuseCandidate()
    {
        await Assert.That(ContributionContinuation.MatchesTitle("Movie", "603", "movie", "603")).IsTrue();
        await Assert.That(ContributionContinuation.MatchesTitle("series", "603", "movie", "603")).IsFalse();
        await Assert.That(ContributionContinuation.MatchesTitle("movie", "604", "movie", "603")).IsFalse();
    }

    internal sealed class MemoryJsRuntime : IJSRuntime, IJSObjectReference
    {
        private readonly Dictionary<(string Store, string Key), string> values = [];
        public bool FailWrites { get; set; }

        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, object?[]? args)
            => InvokeAsync<TValue>(identifier, CancellationToken.None, args);

        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, CancellationToken cancellationToken, object?[]? args)
        {
            object? result = null;
            if (identifier == "import")
            {
                result = this;
            }
            else if (identifier == "get")
            {
                this.values.TryGetValue(((string)args![0]!, (string)args[1]!), out string? json);
                result = json;
            }
            else if (identifier == "set")
            {
                if (this.FailWrites)
                {
                    throw new JSException("Storage full");
                }
                this.values[((string)args![0]!, (string)args[1]!)] = (string)args[2]!;
            }
            else if (identifier == "removeScan")
            {
                var key = ("scans", (string)args![0]!);
                if (this.values.TryGetValue(key, out string? json)
                    && JsonSerializer.Deserialize<LocalDiscScan>(json)!.Id == (string)args[1]!)
                {
                    this.values.Remove(key);
                }
            }
            return ValueTask.FromResult(result is null ? default! : (TValue)result);
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class TestAuthentication(bool signedIn) : AuthenticationStateProvider
    {
        public override Task<AuthenticationState> GetAuthenticationStateAsync()
            => Task.FromResult(new AuthenticationState(new ClaimsPrincipal(
                signedIn ? new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, "test")], "test") : new ClaimsIdentity())));
    }

    private sealed class TestNavigation : NavigationManager
    {
        public bool ForceLoad { get; private set; }
        public TestNavigation() => Initialize("https://localhost/", "https://localhost/contribute");
        protected override void NavigateToCore(string uri, bool forceLoad)
        {
            this.Uri = ToAbsoluteUri(uri).ToString();
            this.ForceLoad = forceLoad;
        }
    }
}