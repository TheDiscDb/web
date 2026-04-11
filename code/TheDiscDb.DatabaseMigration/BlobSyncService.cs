using System.Text.Json;
using System.Text.Json.Serialization;
using Fantastic.FileSystem;
using Microsoft.Extensions.Options;
using TheDiscDb.Data.Import;
using TheDiscDb.ImportModels;

namespace TheDiscDb.DatabaseMigration;

public class BlobSyncService(
    IStaticAssetStore imageStore,
    IFileSystem fileSystem,
    IOptions<DatabaseMigrationOptions> options,
    ILogger<BlobSyncService> logger)
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private int _uploaded;
    private int _skipped;

    public async Task SyncAsync(CancellationToken cancellationToken)
    {
        var root = options.Value.DataDirectoryRoot;
        if (string.IsNullOrEmpty(root))
        {
            logger.LogInformation("DataDirectoryRoot not configured — skipping blob sync");
            return;
        }

        logger.LogInformation("Starting blob sync from {Root}", root);

        string[] mediaTypes = ["movie", "series", "sets"];

        foreach (var mediaType in mediaTypes)
        {
            var dir = fileSystem.Path.Combine(root, mediaType);
            if (!await fileSystem.Directory.Exists(dir, cancellationToken))
            {
                logger.LogInformation("Directory {Dir} not found — skipping", dir);
                continue;
            }

            await foreach (var itemDir in fileSystem.Directory.EnumerateDirectories(dir, cancellationToken))
            {
                try
                {
                    await SyncItemDirectory(itemDir, cancellationToken);
                }
                catch (Exception ex) when (ex is JsonException or IOException)
                {
                    logger.LogWarning(ex, "Error syncing {Dir} — skipping", itemDir);
                }
            }
        }

        logger.LogInformation("Blob sync complete — uploaded: {Uploaded}, already existed: {Skipped}",
            _uploaded, _skipped);
    }

    private async Task SyncItemDirectory(string itemDir, CancellationToken cancellationToken)
    {
        var metadataPath = fileSystem.Path.Combine(itemDir, MetadataFile.Filename);
        var boxsetPath = fileSystem.Path.Combine(itemDir, BoxSetReleaseFile.Filename);

        if (await fileSystem.File.Exists(boxsetPath, cancellationToken))
        {
            await SyncBoxset(itemDir, boxsetPath, cancellationToken);
            return;
        }

        if (!await fileSystem.File.Exists(metadataPath, cancellationToken))
        {
            logger.LogWarning("No metadata.json in {Dir} — skipping", itemDir);
            return;
        }

        var json = await fileSystem.File.ReadAllText(metadataPath, cancellationToken);
        var metadata = JsonSerializer.Deserialize<MetadataFile>(json, JsonOptions);

        if (metadata?.Type == null || metadata.Slug == null)
        {
            logger.LogWarning("Missing Type or Slug in {File} — skipping", metadataPath);
            return;
        }

        // Upload cover image
        var coverPath = fileSystem.Path.Combine(itemDir, "cover.jpg");
        if (await fileSystem.File.Exists(coverPath, cancellationToken))
        {
            var remotePath = $"{metadata.Type}/{metadata.Slug}/cover.jpg";
            await UploadIfMissing(coverPath, remotePath, cancellationToken);
        }

        // Upload release images
        await foreach (var releaseDir in fileSystem.Directory.EnumerateDirectories(itemDir, cancellationToken))
        {
            try
            {
                await SyncReleaseDirectory(releaseDir, metadata.Type, metadata.Slug, cancellationToken);
            }
            catch (Exception ex) when (ex is JsonException or IOException)
            {
                logger.LogWarning(ex, "Error syncing release {Dir} — skipping", releaseDir);
            }
        }
    }

    private async Task SyncReleaseDirectory(string releaseDir, string type, string itemSlug,
        CancellationToken cancellationToken)
    {
        var releasePath = fileSystem.Path.Combine(releaseDir, ReleaseFile.Filename);
        if (!await fileSystem.File.Exists(releasePath, cancellationToken))
            return;

        var json = await fileSystem.File.ReadAllText(releasePath, cancellationToken);
        var release = JsonSerializer.Deserialize<ReleaseFile>(json, JsonOptions);

        if (release?.Slug == null)
            return;

        // Front cover
        var frontPath = fileSystem.Path.Combine(releaseDir, "front.jpg");
        if (await fileSystem.File.Exists(frontPath, cancellationToken))
        {
            var remotePath = $"{type}/{itemSlug}/{release.Slug}.jpg";
            await UploadIfMissing(frontPath, remotePath, cancellationToken);
        }

        // Back cover
        var backPath = fileSystem.Path.Combine(releaseDir, "back.jpg");
        if (await fileSystem.File.Exists(backPath, cancellationToken))
        {
            var remotePath = $"{type}/{itemSlug}/{release.Slug}-back.jpg";
            await UploadIfMissing(backPath, remotePath, cancellationToken);
        }
    }

    private async Task SyncBoxset(string boxsetDir, string boxsetFilePath, CancellationToken cancellationToken)
    {
        var json = await fileSystem.File.ReadAllText(boxsetFilePath, cancellationToken);
        var boxset = JsonSerializer.Deserialize<BoxSetReleaseFile>(json, JsonOptions);

        if (boxset?.Slug == null)
            return;

        // Front cover
        var frontPath = fileSystem.Path.Combine(boxsetDir, "front.jpg");
        if (await fileSystem.File.Exists(frontPath, cancellationToken))
        {
            var remotePath = $"boxset/{boxset.Slug}.jpg";
            await UploadIfMissing(frontPath, remotePath, cancellationToken);
        }

        // Back cover
        var backPath = fileSystem.Path.Combine(boxsetDir, "back.jpg");
        if (await fileSystem.File.Exists(backPath, cancellationToken))
        {
            var remotePath = $"boxset/{boxset.Slug}-back.jpg";
            await UploadIfMissing(backPath, remotePath, cancellationToken);
        }
    }

    private async Task UploadIfMissing(string localPath, string remotePath, CancellationToken cancellationToken)
    {
        if (await imageStore.Exists(remotePath, cancellationToken))
        {
            _skipped++;
            return;
        }

        await imageStore.Save(localPath, remotePath, ContentTypes.ImageContentType, cancellationToken);
        _uploaded++;

        if (_uploaded % 100 == 0)
        {
            logger.LogInformation("Blob sync progress — uploaded: {Uploaded}", _uploaded);
        }
    }
}
