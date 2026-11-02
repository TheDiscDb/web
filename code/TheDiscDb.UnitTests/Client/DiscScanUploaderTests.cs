using System.Net;
using System.Text.Json;
using TheDiscDb.Client.Pages.Contribute;
using TheDiscDb.Client.Services;
using TheDiscDb.Core.DiscHash;
using TheDiscDb.InputModels;

namespace TheDiscDb.UnitTests.Client;

public class DiscScanUploaderTests
{
    [Test]
    public async Task SaveDraftAsync_KnownDisc_SavesReferenceWithoutManifestScan()
    {
        var handler = new CapturingHandler();
        // A null scanner proves the expensive manifest scan is never reached.
        var uploader = new DiscScanUploader(null!, null!, new HttpClient(handler));
        var identifiers = new DiscScanResult(
            DiscFormatConstants.FourK,
            null,
            [new FileHashInfo { Index = 0, Name = "00000.m2ts", Size = 1024, CreationTime = new DateTime(2020, 1, 1) }],
            [new DiscFingerprintFile("BDMV/index.bdmv", 120)],
            null);
        string? checkedHash = null;

        var (result, discHash) = await uploader.SaveDraftAsync(
            [],
            "https://localhost/",
            _ => { },
            CancellationToken.None,
            identifiers,
            isKnownDisc: (hash, _) =>
            {
                checkedHash = hash;
                return Task.FromResult(true);
            });

        await Assert.That(result.Uploaded).IsTrue();
        await Assert.That(result.ExistingDisc).IsTrue();
        await Assert.That(discHash).IsEqualTo(checkedHash!.ToLowerInvariant());
        var draft = JsonSerializer.Deserialize<DiscScanDraft>(handler.Body!, JsonSerializerOptions.Web)!;
        await Assert.That(draft.IsExistingDiscReference).IsTrue();
        await Assert.That(draft.ManifestJson).IsEmpty();
        await Assert.That(draft.Format).IsEqualTo(DiscFormatConstants.FourK);
        await Assert.That(draft.HashFiles.Count).IsEqualTo(1);
    }

    private sealed class CapturingHandler : HttpMessageHandler
    {
        public string? Body { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            this.Body = await request.Content!.ReadAsStringAsync(cancellationToken);
            return new HttpResponseMessage(HttpStatusCode.OK);
        }
    }
}
