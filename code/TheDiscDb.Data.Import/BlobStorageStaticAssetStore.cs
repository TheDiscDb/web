namespace TheDiscDb.Data.Import
{
    using System;
    using System.IO;
    using System.Threading;
    using System.Threading.Tasks;
    using Azure.Storage.Blobs;
    using Azure.Storage.Blobs.Models;
    using Microsoft.Extensions.Options;

    public static class ContentTypes
    {
        public const string ImageContentType = "image/jpeg";
        public const string TextContentType = "text/plain";
        public const string JsonContentType = "application/json";
    }

    public class BlobStorageStaticAssetStore : IStaticAssetStore
    {
        private readonly BlobServiceClient client;
        private readonly IOptions<BlobStorageOptions> options;

        public BlobStorageStaticAssetStore(BlobServiceClient client, IOptions<BlobStorageOptions> options)
        {
            this.client = client ?? throw new System.ArgumentNullException(nameof(client));
            this.options = options ?? throw new System.ArgumentNullException(nameof(options));
        }

        private BlobClient GetClient(string remotePath)
        {
            BlobContainerClient containerClient = this.client.GetBlobContainerClient(this.options.Value.ContainerName);
            BlobClient blobClient = containerClient.GetBlobClient(remotePath);
            return blobClient;
        }

        private bool containerExists = false;

        public string ContainerName
        {
            get => options.Value.ContainerName;
            set
            {
                if (options.Value.ContainerName != value)
                {
                    options.Value.ContainerName = value;
                    containerExists = false; // reset the container exists check
                }
            }
        }

        private async Task EnsureContainerCreated(CancellationToken cancellationToken)
        {
            if (!containerExists)
            {
                BlobContainerClient containerClient = this.client.GetBlobContainerClient(this.options.Value.ContainerName);
                await containerClient.CreateIfNotExistsAsync(cancellationToken: cancellationToken);
                containerExists = true;
            }
        }

        public async Task<bool> Exists(string remotePath, CancellationToken cancellationToken = default)
        {
            await EnsureContainerCreated(cancellationToken);
            var blobClient = GetClient(remotePath);
            return await blobClient.ExistsAsync(cancellationToken);
        }

        public async Task<string> Save(string filePath, string remotePath, string contentType, CancellationToken cancellationToken = default)
        {
            await EnsureContainerCreated(cancellationToken);
            var blobClient = GetClient(remotePath);
            var uploadOptions = new BlobUploadOptions
            {
                HttpHeaders = new BlobHttpHeaders
                {
                    ContentType = contentType
                }
            };

            if (!await blobClient.ExistsAsync(cancellationToken))
            {
                await blobClient.UploadAsync(filePath, uploadOptions, cancellationToken);
            }

            return blobClient.Uri.ToString();
        }

        public async Task<string> Save(Stream stream, string remotePath, string contentType, CancellationToken cancellationToken = default)
        {
            await EnsureContainerCreated(cancellationToken);
            var blobClient = GetClient(remotePath);
            var uploadOptions = new BlobUploadOptions
            {
                HttpHeaders = new BlobHttpHeaders
                {
                    ContentType = contentType
                }
            };

            if (!await blobClient.ExistsAsync(cancellationToken))
            {
                await blobClient.UploadAsync(stream, uploadOptions, cancellationToken);
            }

            return blobClient.Uri.ToString();
        }

        public async Task<BinaryData> Download(string remotePath, CancellationToken cancellationToken = default)
        {
            await EnsureContainerCreated(cancellationToken);
            var blobClient = GetClient(remotePath);
            var response = await blobClient.DownloadContentAsync(cancellationToken);
            return response.Value.Content;
        }
    }
}
