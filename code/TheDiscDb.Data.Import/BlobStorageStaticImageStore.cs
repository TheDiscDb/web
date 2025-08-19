namespace TheDiscDb.Data.Import
{
    using System.IO;
    using System.Threading;
    using System.Threading.Tasks;
    using Azure.Storage.Blobs;
    using Azure.Storage.Blobs.Models;
    using Microsoft.Extensions.Options;

    public class BlobStorageStaticImageStore : IStaticImageStore
    {
        private readonly BlobServiceClient client;
        private readonly IOptions<BlobStorageOptions> options;

        public BlobStorageStaticImageStore(BlobServiceClient client, IOptions<BlobStorageOptions> options)
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
        private async Task EnsureContainerCreated()
        {
            if (!containerExists)
            {
                BlobContainerClient containerClient = this.client.GetBlobContainerClient(this.options.Value.ContainerName);
                await containerClient.CreateIfNotExistsAsync();
                containerExists = true;
            }
        }

        public async Task<bool> Exists(string remotePath, CancellationToken cancellationToken = default)
        {
            await EnsureContainerCreated();
            var blobClient = GetClient(remotePath);
            return await blobClient.ExistsAsync(cancellationToken);
        }

        public async Task<string> SaveImage(string filePath, string remotePath, CancellationToken cancellationToken = default)
        {
            await EnsureContainerCreated();
            var blobClient = GetClient(remotePath);
            var uploadOptions = new BlobUploadOptions
            {
                HttpHeaders = new BlobHttpHeaders
                {
                    ContentType = "image/jpeg"
                }
            };

            if (!await blobClient.ExistsAsync(cancellationToken))
            {
                await blobClient.UploadAsync(filePath, uploadOptions, cancellationToken);
            }

            return blobClient.Uri.ToString();
        }

        public async Task<string> SaveImage(Stream stream, string remotePath, CancellationToken cancellationToken = default)
        {
            await EnsureContainerCreated();
            var blobClient = GetClient(remotePath);
            var uploadOptions = new BlobUploadOptions
            {
                HttpHeaders = new BlobHttpHeaders
                {
                    ContentType = "image/jpeg"
                }
            };

            if (!await blobClient.ExistsAsync(cancellationToken))
            {
                await blobClient.UploadAsync(stream, uploadOptions, cancellationToken);
            }

            return blobClient.Uri.ToString();
        }
    }
}
