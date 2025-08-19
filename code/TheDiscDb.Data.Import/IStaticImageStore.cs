namespace TheDiscDb.Data.Import
{
    using System.IO;
    using System.Threading;
    using System.Threading.Tasks;

    public interface IStaticImageStore
    {
        Task<string> SaveImage(string filePath, string remotePath, CancellationToken cancellationToken = default);
        Task<string> SaveImage(Stream stream, string remotePath, CancellationToken cancellationToken = default);
        Task<bool> Exists(string remotePath, CancellationToken cancellationToken = default);
    }

    public class NullStaticImageStore : IStaticImageStore
    {
        public Task<bool> Exists(string remotePath, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(true);
        }

        public Task<string> SaveImage(string filePath, string remotePath, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(string.Empty);
        }

        public Task<string> SaveImage(Stream stream, string remotePath, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(string.Empty);
        }
    }
}
