namespace TheDiscDb.OpticalDiscManifest.Generation;

public interface IManifestDiscFile
{
    string Path { get; }

    long Size { get; }

    ValueTask<byte[]> ReadBytesAsync(
        long maxAllowedSize,
        CancellationToken cancellationToken = default);
}

public sealed record ManifestGenerationRequest
{
    public required IReadOnlyList<IManifestDiscFile> Files { get; init; }

    public required string ProducerName { get; init; }

    public required string ProducerVersion { get; init; }

    public string? ProducerUri { get; init; }

    public IReadOnlyList<Models.ManifestIdentifier>? Identifiers { get; init; }

    public Action<string>? ReportProgress { get; init; }
}
