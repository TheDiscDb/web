using System.Text.RegularExpressions;
using TheDiscDb.Core.DiscHash;
using TheDiscDb.OpticalDiscManifest.Models;

namespace TheDiscDb.OpticalDiscManifest.Generation;

/// <summary>
/// Computes the pressing identifiers TheDiscDb uses alongside the content hash: the
/// Matrix256 inventory fingerprint and the AACS (Blu-ray/UHD) or DVD Disc ID.
/// </summary>
internal static partial class DiscIdentifierCalculator
{
    private const long MaxDiscIdFileSize = 16 * 1024 * 1024;

    [GeneratedRegex(@"^VIDEO_TS/VTS_(\d{2})_0\.IFO$", RegexOptions.IgnoreCase)]
    private static partial Regex VtsIfoPattern();

    internal static ManifestIdentifier? ComputeFingerprint(
        IReadOnlyList<NormalizedFile> files,
        ICollection<ManifestDiagnostic> diagnostics)
    {
        if (files.Count == 0)
        {
            return null;
        }

        try
        {
            return new ManifestIdentifier
            {
                Kind = ManifestIdentifierKinds.Matrix256,
                Value = DiscFingerprint.Calculate(files.Select(file => new DiscFingerprintFile(file.Path, file.File.Size))),
            };
        }
        catch (ArgumentException ex)
        {
            diagnostics.Add(Unavailable(ManifestIdentifierKinds.Matrix256, ex.Message));
            return null;
        }
    }

    internal static async Task<ManifestIdentifier?> ComputeDiscIdAsync(
        DiscFileCatalog files,
        string format,
        ICollection<ManifestDiagnostic> diagnostics,
        ReadMetrics metrics,
        CancellationToken cancellationToken)
    {
        string kind = format switch
        {
            "dvd" => ManifestIdentifierKinds.DvdDiscId,
            "blu-ray" or "uhd-blu-ray" => ManifestIdentifierKinds.AacsDiscId,
            _ => string.Empty,
        };
        if (kind.Length == 0)
        {
            return null;
        }

        try
        {
            string? value = kind == ManifestIdentifierKinds.DvdDiscId
                ? await ComputeDvdDiscIdAsync(files, metrics, cancellationToken)
                : await ComputeAacsDiscIdAsync(files, metrics, cancellationToken);
            return value is null ? null : new ManifestIdentifier { Kind = kind, Value = value };
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            diagnostics.Add(Unavailable(kind, ex.Message));
            return null;
        }
    }

    private static async Task<string?> ComputeAacsDiscIdAsync(
        DiscFileCatalog files,
        ReadMetrics metrics,
        CancellationToken cancellationToken)
    {
        var unitKey = DiscFileCatalog.Find(files, "AACS/Unit_Key_RO.inf")
            ?? DiscFileCatalog.Find(files, "AACS/DUPLICATE/Unit_Key_RO.inf");

        // Unencrypted discs have no AACS directory and therefore no AACS Disc ID.
        return unitKey is null
            ? null
            : AacsDiscId.Compute(await ReadCompleteAsync(unitKey, metrics, cancellationToken));
    }

    private static async Task<string?> ComputeDvdDiscIdAsync(
        DiscFileCatalog files,
        ReadMetrics metrics,
        CancellationToken cancellationToken)
    {
        var videoTsIfo = DiscFileCatalog.Find(files, "VIDEO_TS/VIDEO_TS.IFO");
        if (videoTsIfo is null)
        {
            return null;
        }

        var videoTsBytes = await ReadCompleteAsync(videoTsIfo, metrics, cancellationToken);
        var vtsByNumber = new SortedDictionary<int, byte[]>();
        foreach (var file in files)
        {
            var match = VtsIfoPattern().Match(file.Path);
            if (match.Success)
            {
                vtsByNumber[int.Parse(match.Groups[1].Value)] = await ReadCompleteAsync(file, metrics, cancellationToken);
            }
        }

        var vtsIfos = new byte[]?[vtsByNumber.Count > 0 ? vtsByNumber.Keys.Max() : 0];
        foreach (var item in vtsByNumber)
        {
            vtsIfos[item.Key - 1] = item.Value;
        }

        return DvdDiscId.Compute(videoTsBytes, vtsIfos);
    }

    private static async Task<byte[]> ReadCompleteAsync(
        NormalizedFile file,
        ReadMetrics metrics,
        CancellationToken cancellationToken)
    {
        if (file.File.Size < 0 || file.File.Size > MaxDiscIdFileSize)
        {
            throw new InvalidDataException($"{file.Path} has an unsupported size ({file.File.Size} bytes).");
        }

        var bytes = await file.File.ReadBytesAsync(MaxDiscIdFileSize, cancellationToken);
        metrics.FilesRead++;
        metrics.BytesRead += bytes.LongLength;

        // A partial read would hash to a different, wrong identifier.
        if (bytes.LongLength != file.File.Size)
        {
            throw new InvalidDataException($"Expected {file.File.Size} bytes from {file.Path} but read {bytes.LongLength}.");
        }

        return bytes;
    }

    private static ManifestDiagnostic Unavailable(string kind, string reason)
        => new()
        {
            Severity = "info",
            Code = "ODM_DISC_IDENTIFIER_UNAVAILABLE",
            Message = $"The {kind} identifier was not computed: {reason}",
        };
}
