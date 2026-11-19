using System.Net;
using System.Net.Http.Json;
using TheDiscDb.Client.Services;
using TheDiscDb.Core.DiscHash;

namespace TheDiscDb.UnitTests.Client;

public class DiscScanHandoffTests
{
    [Test]
    public async Task PrepareAsync_NewScan_UploadsBeforeCleaningLocalCopy()
    {
        await using var storage = new BrowserScanStorage(new BrowserScanFlowTests.MemoryJsRuntime());
        var draft = new DiscScanDraft { DiscHash = "ABC", ManifestJson = "manifest", Fingerprint = "fingerprint" };
        await storage.SaveScanAsync(draft);
        var local = await storage.GetScanAsync("abc");
        var handler = new ScanHandler();
        var handoff = new DiscScanHandoff(storage, new HttpClient(handler) { BaseAddress = new Uri("https://localhost") });
        var continuation = new ContributionContinuation("movie", "603", "abc", LocalScanId: local!.Id);

        var destination = await handoff.PrepareAsync(continuation);

        await Assert.That(destination).IsEqualTo(continuation.DetailsUrl);
        await Assert.That(handler.Uploaded!.ManifestJson).IsEqualTo("manifest");
        await Assert.That(await storage.GetScanAsync("abc")).IsNull();
    }

    [Test]
    [Arguments(HttpStatusCode.ServiceUnavailable)]
    [Arguments(HttpStatusCode.Unauthorized)]
    public async Task PrepareAsync_UploadFails_RetainsScanForRetry(HttpStatusCode status)
    {
        await using var storage = new BrowserScanStorage(new BrowserScanFlowTests.MemoryJsRuntime());
        await storage.SaveScanAsync(new DiscScanDraft { DiscHash = "ABC", ManifestJson = "manifest" });
        var local = await storage.GetScanAsync("abc");
        var handler = new ScanHandler { UploadStatus = status };
        var handoff = new DiscScanHandoff(storage, new HttpClient(handler) { BaseAddress = new Uri("https://localhost") });

        await Assert.That(async () => { await handoff.PrepareAsync(new ContributionContinuation("movie", "603", "abc", LocalScanId: local!.Id)); })
            .Throws<HttpRequestException>();
        await Assert.That((await storage.GetScanAsync("abc"))!.Draft.ManifestJson).IsEqualTo("manifest");
    }

    [Test]
    public async Task PrepareAsync_AlreadyLinked_ResumesContributionWithoutOverwriting()
    {
        await using var storage = new BrowserScanStorage(new BrowserScanFlowTests.MemoryJsRuntime());
        var handler = new ScanHandler { Existing = new DiscScanDraft { DiscHash = "ABC", ContributionId = "AbC" } };
        var handoff = new DiscScanHandoff(storage, new HttpClient(handler) { BaseAddress = new Uri("https://localhost") });

        var destination = await handoff.PrepareAsync(new ContributionContinuation("movie", "603", "abc"));

        await Assert.That(destination).IsEqualTo("/contribution/AbC");
        await Assert.That(handler.Uploaded).IsNull();
    }

    [Test]
    public async Task PrepareAsync_RetryAfterSuccessfulUpload_DoesNotPostAgain()
    {
        await using var storage = new BrowserScanStorage(new BrowserScanFlowTests.MemoryJsRuntime());
        var handler = new ScanHandler { Existing = new DiscScanDraft { DiscHash = "ABC", ManifestJson = "manifest" } };
        var handoff = new DiscScanHandoff(storage, new HttpClient(handler) { BaseAddress = new Uri("https://localhost") });
        var continuation = new ContributionContinuation("series", "123", "abc", "box", true);

        var destination = await handoff.PrepareAsync(continuation);

        await Assert.That(destination).IsEqualTo(continuation.DetailsUrl);
        await Assert.That(handler.Uploaded).IsNull();
    }

    [Test]
    public async Task PrepareAsync_NewerLocalScan_DoesNotUploadOrDeleteIt()
    {
        await using var storage = new BrowserScanStorage(new BrowserScanFlowTests.MemoryJsRuntime());
        await storage.SaveScanAsync(new DiscScanDraft { DiscHash = "ABC", ManifestJson = "new scan" });
        var handler = new ScanHandler();
        var handoff = new DiscScanHandoff(storage, new HttpClient(handler) { BaseAddress = new Uri("https://localhost") });

        await Assert.That(async () => { await handoff.PrepareAsync(new ContributionContinuation("movie", "603", "abc", LocalScanId: "old")); })
            .Throws<InvalidOperationException>();
        await Assert.That(handler.Uploaded).IsNull();
        await Assert.That((await storage.GetScanAsync("abc"))!.Draft.ManifestJson).IsEqualTo("new scan");
    }

    private sealed class ScanHandler : HttpMessageHandler
    {
        public DiscScanDraft? Existing { get; init; }
        public DiscScanDraft? Uploaded { get; private set; }
        public HttpStatusCode UploadStatus { get; init; } = HttpStatusCode.OK;

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (request.Method == HttpMethod.Get)
            {
                return this.Existing is null
                    ? new HttpResponseMessage(HttpStatusCode.NotFound)
                    : new HttpResponseMessage(HttpStatusCode.OK) { Content = JsonContent.Create(this.Existing) };
            }
            this.Uploaded = await request.Content!.ReadFromJsonAsync<DiscScanDraft>(cancellationToken);
            return new HttpResponseMessage(this.UploadStatus);
        }
    }
}