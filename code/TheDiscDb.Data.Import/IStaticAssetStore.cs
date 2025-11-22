namespace TheDiscDb.Data.Import
{
    using System;
    using System.IO;
    using System.Threading;
    using System.Threading.Tasks;

    public interface IStaticAssetStore
    {
        Task<string> Save(string filePath, string remotePath, string contentType, CancellationToken cancellationToken = default);
        Task<string> Save(Stream stream, string remotePath, string contentType, CancellationToken cancellationToken = default);
        Task<bool> Exists(string remotePath, CancellationToken cancellationToken = default);
        string ContainerName { get; set; }
        Task<BinaryData> Download(string remotePath, CancellationToken cancellationToken = default);
        Task Delete(string remotePath, CancellationToken cancellationToken = default);
    }

    public class NullStaticAssetStore : IStaticAssetStore
    {
        public string ContainerName { get; set; } = "";

        public Task Delete(string remotePath, CancellationToken cancellationToken = default)
        {
            return Task.CompletedTask;
        }

        public Task<BinaryData> Download(string remotePath, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(BinaryData.Empty);
        }

        public Task<bool> Exists(string remotePath, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(true);
        }

        public Task<string> Save(string filePath, string remotePath, string contentType, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(string.Empty);
        }

        public Task<string> Save(Stream stream, string remotePath, string contentType, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(string.Empty);
        }
    }
}
