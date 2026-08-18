namespace TheDiscDb.UnitTests.Services;

using System.Text.Json;
using TheDiscDb.Data.Import;
using TheDiscDb.Services;

public class AmazonImporterTests
{
    [Test]
    public async Task GetProductMetadataAsync_CachedAsin_ReturnsCachedMetadata()
    {
        var metadata = new AmazonProductMetadata
        {
            Asin = "B012345678",
            Title = "The Natural - 4K + Blu-ray + Digital",
            Upc = "123456789012",
        };
        var store = new CachedAssetStore(
            "cache/amazon/B012345678.json",
            JsonSerializer.Serialize(metadata));
        var importer = new AmazonImporter(store);

        AmazonProductMetadata? result = await importer.GetProductMetadataAsync(" b012345678 ");

        await Assert.That(result).IsNotNull();
        await Assert.That(result!.Title).IsEqualTo("The Natural - 4K + Blu-ray + Digital");
        await Assert.That(result.MediaTitle).IsEqualTo("The Natural");
        await Assert.That(store.DownloadedPath).IsEqualTo("cache/amazon/B012345678.json");
        await Assert.That(store.SaveCount).IsEqualTo(0);
    }

    private sealed class CachedAssetStore(string path, string content) : IStaticAssetStore
    {
        public string ContainerName { get; set; } = "images";
        public string? DownloadedPath { get; private set; }
        public int SaveCount { get; private set; }

        public Task<bool> Exists(string remotePath, CancellationToken cancellationToken = default) =>
            Task.FromResult(remotePath == path);

        public Task<BinaryData> Download(string remotePath, CancellationToken cancellationToken = default)
        {
            this.DownloadedPath = remotePath;
            return Task.FromResult(BinaryData.FromString(content));
        }

        public Task<string> Save(
            Stream stream,
            string remotePath,
            string contentType,
            CancellationToken cancellationToken = default)
        {
            this.SaveCount++;
            return Task.FromResult(remotePath);
        }

        public Task<string> Save(
            string filePath,
            string remotePath,
            string contentType,
            CancellationToken cancellationToken = default) =>
            throw new NotImplementedException();

        public Task Delete(string remotePath, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;
    }
}
