using TheDiscDb.OpticalDiscManifest.Generation;

namespace TheDiscDb.Client.Pages.Contribute;

public sealed class DiscScanFile : IManifestDiscFile
{
    private readonly Func<long, CancellationToken, ValueTask<byte[]>> readBytes;

    public DiscScanFile(
        string path,
        string name,
        long size,
        DateTime lastModified,
        Func<long, CancellationToken, ValueTask<byte[]>> readBytes)
    {
        Path = path;
        Name = name;
        Size = size;
        LastModified = lastModified;
        this.readBytes = readBytes;
    }

    public string Path { get; }

    public string Name { get; }

    public long Size { get; }

    public DateTime LastModified { get; }

    public ValueTask<byte[]> ReadBytesAsync(long maxAllowedSize, CancellationToken cancellationToken = default)
        => this.readBytes(maxAllowedSize, cancellationToken);
}

public sealed class DiscFileSelection : IAsyncDisposable
{
    private readonly Func<ValueTask>? dispose;

    public DiscFileSelection(IReadOnlyList<DiscScanFile> files, Func<ValueTask>? dispose = null)
    {
        Files = files;
        this.dispose = dispose;
    }

    public IReadOnlyList<DiscScanFile> Files { get; }

    public ValueTask DisposeAsync() => this.dispose?.Invoke() ?? ValueTask.CompletedTask;
}
