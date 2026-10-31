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

        /// <summary>
        /// Reads mutable content and its ETag atomically, or returns null when the object is absent.
        /// Stores without conditional-write support must fail rather than fall back to Save.
        /// </summary>
        Task<MutableAssetSnapshot?> ReadForUpdate(string remotePath, CancellationToken cancellationToken = default)
            => throw new NotSupportedException("This asset store does not support conditional updates.");

        /// <summary>
        /// Atomically writes content only if the current ETag matches expectedETag.
        /// A null expectedETag means create only if absent. Returns false only for a
        /// conditional conflict; all other failures (including cancellation) propagate.
        /// Unlike Save, this explicitly supports replacing an existing object.
        /// </summary>
        Task<bool> TryWrite(BinaryData content, string remotePath, string contentType, string? expectedETag, CancellationToken cancellationToken = default)
            => throw new NotSupportedException("This asset store does not support conditional updates.");
    }

    public sealed record MutableAssetSnapshot(BinaryData Content, string ETag);

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
