namespace TheDiscDb.Data.Import.Pipeline
{
    using System;
    using System.Linq;
    using System.Text.Json;
    using System.Threading;
    using System.Threading.Tasks;
    using Fantastic.FileSystem;
    using TheDiscDb.ImportModels;

    public class CoverImageUploadMiddleware : IMiddleware
    {
        private readonly IFileSystem fileSystem;
        private readonly IStaticAssetStore imageStore;

        public CoverImageUploadMiddleware(IFileSystem fileSystem, IStaticAssetStore imageStore)
        {
            this.fileSystem = fileSystem ?? throw new ArgumentNullException(nameof(fileSystem));
            this.imageStore = imageStore ?? throw new ArgumentNullException(nameof(imageStore));
        }

        public Func<ImportItem, CancellationToken, Task> Next { get; set; } = (_, _) => Task.CompletedTask;

        public async Task Process(ImportItem item, CancellationToken cancellationToken)
        {
            if (item.MediaItem != null)
            {
                string coverImagePath = this.fileSystem.Path.Combine(item.BasePath, "cover.jpg");
                string metaDataFile = this.fileSystem.Path.Combine(item.BasePath, MetadataFile.Filename);
                if (await this.fileSystem.File.Exists(coverImagePath, cancellationToken))
                {
                    string remotePath = string.Format("{0}/{1}/{2}", item.Metadata.Type, item.Metadata.Slug, "cover.jpg");

                    if (!await this.imageStore.Exists(remotePath, cancellationToken))
                    {
                        await this.imageStore.Save(coverImagePath, remotePath, ContentTypes.ImageContentType, cancellationToken);
                    }

                    if (item.MediaItem.ImageUrl != remotePath)
                    {
                        item.Metadata.ImageUrl = remotePath;
                        item.MediaItem.ImageUrl = remotePath;

                        // re-save the metadata file
                        string json = JsonSerializer.Serialize(item.Metadata, JsonHelper.JsonOptions);
                        await this.fileSystem.File.WriteAllText(metaDataFile, json, cancellationToken);
                    }
                }

                foreach (var releaseFolder in await this.fileSystem.Directory.GetDirectories(item.BasePath))
                {
                    string frontCoverPath = this.fileSystem.Path.Combine(releaseFolder, "front.jpg");
                    if (await this.fileSystem.File.Exists(frontCoverPath, cancellationToken))
                    {
                        string releaseFilePath = this.fileSystem.Path.Combine(releaseFolder, ReleaseFile.Filename);
                        string json = await this.fileSystem.File.ReadAllText(releaseFilePath, cancellationToken);
                        ReleaseFile? releaseFile = JsonSerializer.Deserialize<ReleaseFile>(json, JsonHelper.JsonOptions);

                        var release = item.MediaItem.Releases.FirstOrDefault(r => r.Slug == releaseFile?.Slug);
                        if (release == null || releaseFile == null)
                        {
                            continue;
                        }

                        string remotePath = string.Format("{0}/{1}/{2}.jpg", item.MediaItem.Type, item.MediaItem.Slug, release.Slug);

                        if (!await this.imageStore.Exists(remotePath, cancellationToken))
                        {
                            await this.imageStore.Save(frontCoverPath, remotePath, ContentTypes.ImageContentType, cancellationToken);
                        }
                        release.ImageUrl = remotePath;

                        if (releaseFile.ImageUrl != remotePath)
                        {
                            releaseFile.ImageUrl = remotePath;
                            // re-save the release file
                            json = JsonSerializer.Serialize(releaseFile, JsonHelper.JsonOptions);
                            await this.fileSystem.File.WriteAllText(releaseFilePath, json, cancellationToken);
                        }
                    }

                    // TODO: save back cover if it exists
                    string backCoverPath = this.fileSystem.Path.Combine(releaseFolder, "back.jpg");
                    if (await this.fileSystem.File.Exists(backCoverPath, cancellationToken))
                    {
                        string releaseFilePath = this.fileSystem.Path.Combine(releaseFolder, ReleaseFile.Filename);
                        string json = await this.fileSystem.File.ReadAllText(releaseFilePath, cancellationToken);
                        ReleaseFile? releaseFile = JsonSerializer.Deserialize<ReleaseFile>(json, JsonHelper.JsonOptions);

                        var release = item.MediaItem.Releases.FirstOrDefault(r => r.Slug == releaseFile?.Slug);
                        if (release == null || releaseFile == null)
                        {
                            continue;
                        }

                        string remotePath = string.Format("{0}/{1}/{2}-back.jpg", item.MediaItem.Type, item.MediaItem.Slug, release.Slug);

                        if (!await this.imageStore.Exists(remotePath, cancellationToken))
                        {
                            await this.imageStore.Save(backCoverPath, remotePath, ContentTypes.ImageContentType, cancellationToken);
                        }
                    }
                }
            }
            else if (item.Boxset != null)
            {
                string imagePath = this.fileSystem.Path.Combine(item.BasePath, "front.jpg");
                if (await this.fileSystem.File.Exists(imagePath, cancellationToken))
                {
                    string remotePath = string.Format("boxset/{0}.jpg", item.Boxset.Slug);

                    BoxSetReleaseFile? file = JsonSerializer.Deserialize<BoxSetReleaseFile>(await this.fileSystem.File.ReadAllText(this.fileSystem.Path.Combine(item.BasePath, BoxSetReleaseFile.Filename), cancellationToken), JsonHelper.JsonOptions);

                    if (!await this.imageStore.Exists(remotePath, cancellationToken))
                    {
                        await this.imageStore.Save(imagePath, remotePath, ContentTypes.ImageContentType, cancellationToken);
                    }

                    if (item.Boxset.Release != null)
                    {
                        item.Boxset.Release.ImageUrl = remotePath;
                    }
                    item.Boxset.ImageUrl = remotePath;

                    if (file != null && file.ImageUrl != remotePath)
                    {
                        file.ImageUrl = remotePath;

                        // re-save the boxset file
                        string json = JsonSerializer.Serialize(file, JsonHelper.JsonOptions);
                        await this.fileSystem.File.WriteAllText(this.fileSystem.Path.Combine(item.BasePath, BoxSetReleaseFile.Filename), json, cancellationToken);
                    }
                }
            }

            await this.Next(item, cancellationToken);
        }
    }
}

