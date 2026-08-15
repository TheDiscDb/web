using System.Text.RegularExpressions;
using TheDiscDb.Core.DiscHash;
using TheDiscDb.InputModels;

namespace TheDiscDb.Client.Pages.Contribute;

public sealed record DiscScanResult(
    string? Format,
    string? GlobalDiscId,
    IReadOnlyList<FileHashInfo> HashFiles,
    string? Error);

public static partial class DiscScanner
{
    public const long MaxDiscIdFileSize = 16 * 1024 * 1024;

    [GeneratedRegex(@"^VIDEO_TS/VTS_(\d{2})_0\.IFO$", RegexOptions.IgnoreCase)]
    private static partial Regex VtsIfoPattern();

    public static async Task<DiscScanResult> ScanAsync(
        IReadOnlyList<DiscScanFile> files,
        CancellationToken cancellationToken = default)
    {
        var normalized = files.Select(file => new NormalizedDiscFile(
            file,
            file.Path.Replace('\\', '/').Trim('/'))).ToArray();

        var bluRayFiles = normalized
            .Where(item => IsDirectChild(item.Path, "BDMV/STREAM")
                && item.File.Name.EndsWith(".m2ts", StringComparison.OrdinalIgnoreCase))
            .Select(item => item.File)
            .ToArray();

        if (bluRayFiles.Length > 0)
        {
            var globalDiscId = await TryComputeAacsDiscId(normalized, cancellationToken);
            return new DiscScanResult(
                DiscFormatConstants.BluRay,
                globalDiscId,
                CreateHashFiles(bluRayFiles),
                null);
        }

        if (normalized.Any(item => item.Path.StartsWith("BDMV/", StringComparison.OrdinalIgnoreCase)
            || item.Path.StartsWith("AACS/", StringComparison.OrdinalIgnoreCase)))
        {
            return new DiscScanResult(
                null,
                null,
                Array.Empty<FileHashInfo>(),
                "No BDMV/STREAM files were found.");
        }

        var dvdFiles = normalized
            .Where(item => IsDirectChild(item.Path, "VIDEO_TS"))
            .Select(item => item.File)
            .ToArray();

        if (dvdFiles.Length > 0)
        {
            var globalDiscId = await TryComputeDvdDiscId(normalized, cancellationToken);
            return new DiscScanResult(
                DiscFormatConstants.Dvd,
                globalDiscId,
                CreateHashFiles(dvdFiles),
                null);
        }

        return new DiscScanResult(
            null,
            null,
            Array.Empty<FileHashInfo>(),
            "No BDMV or VIDEO_TS folder found. Pick the disc root (the folder containing BDMV/AACS or VIDEO_TS).");
    }

    private static IReadOnlyList<FileHashInfo> CreateHashFiles(
        IReadOnlyList<DiscScanFile> files)
        => files.Select((file, index) => new FileHashInfo
        {
            Index = index,
            Name = file.Name,
            Size = file.Size,
            CreationTime = file.LastModified,
        }).ToArray();

    private static async Task<string?> TryComputeAacsDiscId(
        IReadOnlyList<NormalizedDiscFile> files,
        CancellationToken cancellationToken)
    {
        try
        {
            var unitKey = FindExact(files, "AACS/Unit_Key_RO.inf")
                ?? FindExact(files, "AACS/DUPLICATE/Unit_Key_RO.inf");
            if (unitKey is null)
            {
                return null;
            }

            return AacsDiscId.Compute(
                await unitKey.ReadBytesAsync(MaxDiscIdFileSize, cancellationToken));
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            Console.WriteLine($"AACS Disc ID computation skipped: {ex.Message}");
            return null;
        }
    }

    private static async Task<string?> TryComputeDvdDiscId(
        IReadOnlyList<NormalizedDiscFile> files,
        CancellationToken cancellationToken)
    {
        try
        {
            var videoTsIfo = FindExact(files, "VIDEO_TS/VIDEO_TS.IFO");
            if (videoTsIfo is null)
            {
                return null;
            }

            var videoTsBytes = await videoTsIfo.ReadBytesAsync(
                MaxDiscIdFileSize,
                cancellationToken);

            var vtsByNumber = new SortedDictionary<int, byte[]>();
            foreach (var item in files)
            {
                var match = VtsIfoPattern().Match(item.Path);
                if (match.Success)
                {
                    vtsByNumber[int.Parse(match.Groups[1].Value)] =
                        await item.File.ReadBytesAsync(MaxDiscIdFileSize, cancellationToken);
                }
            }

            int maxNumber = vtsByNumber.Count > 0 ? vtsByNumber.Keys.Max() : 0;
            var vtsIfos = new byte[]?[maxNumber];
            foreach (var item in vtsByNumber)
            {
                vtsIfos[item.Key - 1] = item.Value;
            }

            return DvdDiscId.Compute(videoTsBytes, vtsIfos);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            Console.WriteLine($"DVD Disc ID computation skipped: {ex.Message}");
            return null;
        }
    }

    private static bool IsDirectChild(string path, string directory)
    {
        if (!path.StartsWith($"{directory}/", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return path[(directory.Length + 1)..].IndexOf('/') < 0;
    }

    private static DiscScanFile? FindExact(
        IEnumerable<NormalizedDiscFile> files,
        string path)
        => files.FirstOrDefault(item =>
            string.Equals(item.Path, path, StringComparison.OrdinalIgnoreCase))?.File;

    private sealed record NormalizedDiscFile(DiscScanFile File, string Path);
}
