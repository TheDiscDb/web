using System.Net;
using System.Net.Http.Headers;
using Azure;
using Azure.Core.Pipeline;
using Azure.Storage.Blobs;
using Microsoft.Extensions.Options;
using TheDiscDb.Data.Import;

namespace TheDiscDb.UnitTests.Services.DataImport;

public class BlobStorageStaticAssetStoreTests
{
    [Test]
    public async Task ReadForUpdate_ReturnsContentAndETagFromSameResponse()
    {
        using var handler = new BlobHandler(request =>
        {
            if (request.Method != HttpMethod.Get)
            {
                throw new InvalidOperationException("Mutable reads must not use a separate properties request.");
            }
            return Response(HttpStatusCode.OK, "{\"comparisons\":[]}", etag: "\"version-1\"");
        });
        var store = CreateStore(handler);

        var snapshot = (await store.ReadForUpdate("history.json"))!;

        await Assert.That(snapshot.Content.ToString()).IsEqualTo("{\"comparisons\":[]}");
        await Assert.That(snapshot.ETag).IsEqualTo("\"version-1\"");
        await Assert.That(handler.AssetRequests).IsEqualTo(1);
    }

    [Test]
    public async Task ReadForUpdate_ReturnsNullOnlyForMissingBlob()
    {
        using var handler = new BlobHandler(_ => Response(HttpStatusCode.NotFound, errorCode: "BlobNotFound"));
        var store = CreateStore(handler);

        await Assert.That(await store.ReadForUpdate("missing.json")).IsNull();
    }

    [Test]
    public async Task ReadForUpdate_PropagatesOtherNotFoundErrors()
    {
        using var handler = new BlobHandler(_ => Response(HttpStatusCode.NotFound, errorCode: "ContainerNotFound"));
        var store = CreateStore(handler);

        var exception = await Assert.ThrowsAsync<RequestFailedException>(() => store.ReadForUpdate("history.json"));
        await Assert.That(exception!.ErrorCode).IsEqualTo("ContainerNotFound");
    }

    [Test]
    [Arguments(null)]
    [Arguments("\"version-1\"")]
    public async Task TryWrite_UsesExplicitCreateOrMatchCondition(string? expectedETag)
    {
        string? ifMatch = null;
        string? ifNoneMatch = null;
        string? contentType = null;
        string? body = null;
        using var handler = new BlobHandler(request =>
        {
            if (request.Method != HttpMethod.Put)
            {
                throw new InvalidOperationException("Mutable writes must not check existence or delete.");
            }
            ifMatch = request.Headers.IfMatch.SingleOrDefault()?.ToString();
            ifNoneMatch = request.Headers.IfNoneMatch.SingleOrDefault()?.ToString();
            contentType = request.Headers.GetValues("x-ms-blob-content-type").Single();
            body = request.Content!.ReadAsStringAsync().GetAwaiter().GetResult();
            return Response(HttpStatusCode.Created, etag: "\"version-2\"");
        });
        var store = CreateStore(handler);

        bool written = await store.TryWrite(BinaryData.FromString("{\"comparisons\":[]}"), "history.json", ContentTypes.JsonContentType, expectedETag);

        await Assert.That(written).IsTrue();
        await Assert.That(ifMatch).IsEqualTo(expectedETag);
        await Assert.That(ifNoneMatch).IsEqualTo(expectedETag is null ? "*" : null);
        await Assert.That(contentType).IsEqualTo(ContentTypes.JsonContentType);
        await Assert.That(body).IsEqualTo("{\"comparisons\":[]}");
        await Assert.That(handler.AssetRequests).IsEqualTo(1);
    }

    [Test]
    [Arguments(412, "ConditionNotMet", "\"version-1\"")]
    [Arguments(412, "ConditionNotMet", null)]
    [Arguments(409, "BlobAlreadyExists", null)]
    public async Task TryWrite_ReturnsFalseForConditionalConflicts(int status, string errorCode, string? expectedETag)
    {
        using var handler = new BlobHandler(_ => Response((HttpStatusCode)status, errorCode: errorCode));
        var store = CreateStore(handler);

        await Assert.That(await store.TryWrite(BinaryData.FromString("{}"), "history.json", ContentTypes.JsonContentType, expectedETag)).IsFalse();
        await Assert.That(handler.AssetRequests).IsEqualTo(1);
    }

    [Test]
    [Arguments(403, "AuthorizationFailure")]
    [Arguments(500, "InternalError")]
    [Arguments(412, "LeaseIdMissing")]
    [Arguments(409, "ContainerBeingDeleted")]
    public async Task TryWrite_PropagatesNonConflictFailures(int status, string errorCode)
    {
        using var handler = new BlobHandler(_ => Response((HttpStatusCode)status, errorCode: errorCode));
        var store = CreateStore(handler);

        var exception = await Assert.ThrowsAsync<RequestFailedException>(() =>
            store.TryWrite(BinaryData.FromString("{}"), "history.json", ContentTypes.JsonContentType, "\"version-1\""));

        await Assert.That(exception!.Status).IsEqualTo(status);
        await Assert.That(exception.ErrorCode).IsEqualTo(errorCode);
        await Assert.That(handler.AssetRequests).IsEqualTo(1);
    }

    [Test]
    public async Task Save_StillSkipsExistingBlob()
    {
        using var handler = new BlobHandler(request =>
        {
            if (request.Method != HttpMethod.Head)
            {
                throw new InvalidOperationException("Save must not overwrite an existing blob.");
            }
            return Response(HttpStatusCode.OK, etag: "\"version-1\"");
        });
        var store = CreateStore(handler);
        using var stream = new MemoryStream("{}"u8.ToArray());

        await Assert.That(await store.Save(stream, "history.json", ContentTypes.JsonContentType)).IsEqualTo("history.json");
        await Assert.That(handler.AssetRequests).IsEqualTo(1);
    }

    private static BlobStorageStaticAssetStore CreateStore(BlobHandler handler)
    {
        var options = new BlobClientOptions
        {
            Transport = new HttpClientTransport(new HttpClient(handler)),
            Retry = { MaxRetries = 0 }
        };
        var client = new BlobServiceClient(new Uri("https://storage.invalid"), options);
        return new BlobStorageStaticAssetStore(client, Options.Create(new BlobStorageOptions { ContainerName = "tests" }));
    }

    private static HttpResponseMessage Response(HttpStatusCode status, string body = "", string? errorCode = null, string? etag = null)
    {
        if (errorCode is not null)
        {
            body = $"<Error><Code>{errorCode}</Code><Message>Storage request failed.</Message></Error>";
        }
        var response = new HttpResponseMessage(status) { Content = new StringContent(body) };
        response.Content.Headers.ContentType = new MediaTypeHeaderValue(errorCode is null ? "application/json" : "application/xml");
        if (etag is not null)
        {
            response.Headers.ETag = new EntityTagHeaderValue(etag);
        }
        if (errorCode is not null)
        {
            response.Headers.Add("x-ms-error-code", errorCode);
        }
        return response;
    }

    // Exercise the real Azure SDK request/response path without a network or storage dependency.
    private sealed class BlobHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        public int AssetRequests { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (request.Method == HttpMethod.Put && request.RequestUri!.Query.Contains("restype=container"))
            {
                return Task.FromResult(Response(HttpStatusCode.Created));
            }
            this.AssetRequests++;
            return Task.FromResult(respond(request));
        }
    }
}
