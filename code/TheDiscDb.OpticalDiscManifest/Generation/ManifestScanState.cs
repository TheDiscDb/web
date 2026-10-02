namespace TheDiscDb.OpticalDiscManifest.Generation;

/// <param name="EnumerationIndex">
/// Position of the file in the caller's enumeration. Directory pickers enumerate a disc
/// in its on-disc directory order, which is the order MakeMKV lists titles in.
/// </param>
internal sealed record NormalizedFile(string Path, IManifestDiscFile File, int EnumerationIndex);

internal sealed class ReadMetrics
{
    public int FilesRead { get; set; }

    public long BytesRead { get; set; }
}
