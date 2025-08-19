namespace TheDiscDb.Data.Import
{
    using Azure.Storage.Blobs;
    using Azure.Storage.Blobs.Models;
    using Microsoft.Extensions.Options;
    using System;
    using System.IO;
    using System.Threading;
    using System.Threading.Tasks;

    public class BlobStorageOptions
    {
        public string ConnectionString { get; set; }
        public string Container { get; set; }
    }

    public class BlobStorageImageStore : IStaticImageStore
    {
        private readonly BlobServiceClient client;
        private readonly IOptions<BlobStorageOptions> options;

        public BlobStorageImageStore(IOptions<BlobStorageOptions> options)
        {
            this.client = new BlobServiceClient(options.Value.ConnectionString);
            this.options = options ?? throw new ArgumentNullException(nameof(options));
        }

        private BlobClient GetClient(string remotePath)
        {
            BlobContainerClient containerClient = this.client.GetBlobContainerClient(this.options.Value.Container);
            BlobClient blobClient = containerClient.GetBlobClient(remotePath);
            return blobClient;
        }

        public async Task<bool> Exists(string remotePath, CancellationToken cancellationToken = default)
        {
            var blobClient = GetClient(remotePath);
            return await blobClient.ExistsAsync(cancellationToken);
        }

        public async Task<string> SaveImage(string filePath, string remotePath, CancellationToken cancellationToken = default)
        {
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
