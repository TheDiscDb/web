using TheDiscDb.Client.Pages.Contribute;
using TheDiscDb.InputModels;
using TheDiscDb.OpticalDiscManifest.Generation;
using TheDiscDb.OpticalDiscManifest.Models;

namespace TheDiscDb.Client.Services;

public sealed class BrowserOpticalDiscManifestScanner
{
    private readonly OpticalDiscManifestGenerator generator;

    public BrowserOpticalDiscManifestScanner(OpticalDiscManifestGenerator generator)
    {
        this.generator = generator;
    }

    public async Task<ManifestGenerationResult> ScanAsync(
        IReadOnlyList<DiscScanFile> files,
        Action<string>? reportProgress = null,
        CancellationToken cancellationToken = default)
    {
        reportProgress?.Invoke("Calculating disc identifiers");
        var identifierScan = await DiscScanner.ScanAsync(files, cancellationToken);
        var identifiers = CreateIdentifiers(files, identifierScan);

        return await generator.GenerateAsync(
            new ManifestGenerationRequest
            {
                Files = files,
                ProducerName = "TheDiscDb browser proof of concept",
                ProducerVersion = typeof(BrowserOpticalDiscManifestScanner)
                    .Assembly
                    .GetName()
                    .Version?
                    .ToString() ?? "0.0.0",
                ProducerUri = "https://thediscdb.com/",
                Identifiers = identifiers,
                ReportProgress = reportProgress,
            },
            cancellationToken);
    }

    private static IReadOnlyList<ManifestIdentifier>? CreateIdentifiers(
        IReadOnlyList<DiscScanFile> files,
        DiscScanResult scan)
    {
        if (string.IsNullOrWhiteSpace(scan.GlobalDiscId))
        {
            return null;
        }

        string kind;
        IReadOnlyList<string> inputPaths;
        if (scan.Format == DiscFormatConstants.Dvd)
        {
            kind = "dvd-disc-id";
            inputPaths = files
                .Select(file => OpticalDiscManifestGenerator.NormalizePath(file.Path))
                .Where(path => path.Equals("VIDEO_TS/VIDEO_TS.IFO", StringComparison.OrdinalIgnoreCase)
                    || (path.StartsWith("VIDEO_TS/VTS_", StringComparison.OrdinalIgnoreCase)
                        && path.EndsWith("_0.IFO", StringComparison.OrdinalIgnoreCase)))
                .OrderBy(path => path, StringComparer.Ordinal)
                .ToArray();
        }
        else
        {
            kind = "aacs-disc-id";
            inputPaths = files
                .Select(file => OpticalDiscManifestGenerator.NormalizePath(file.Path))
                .Where(path => path.Equals("AACS/Unit_Key_RO.inf", StringComparison.OrdinalIgnoreCase)
                    || path.Equals("AACS/DUPLICATE/Unit_Key_RO.inf", StringComparison.OrdinalIgnoreCase))
                .Take(1)
                .ToArray();
        }

        return
        [
            new ManifestIdentifier
            {
                Kind = kind,
                Value = scan.GlobalDiscId,
                ComputedBy = "producer",
                InputPaths = inputPaths,
            },
        ];
    }
}
