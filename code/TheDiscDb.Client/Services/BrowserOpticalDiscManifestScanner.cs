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
        var identifiers = CreateIdentifiers(identifierScan);

        return await generator.GenerateAsync(
            new ManifestGenerationRequest
            {
                Files = files,
                ProducerName = "thediscdb",
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
        DiscScanResult scan)
    {
        if (string.IsNullOrWhiteSpace(scan.GlobalDiscId))
        {
            return null;
        }

        string kind;
        if (scan.Format == DiscFormatConstants.Dvd)
        {
            kind = "dvd-disc-id";
        }
        else
        {
            kind = "aacs-disc-id";
        }

        return
        [
            new ManifestIdentifier
            {
                Kind = kind,
                Value = scan.GlobalDiscId,
            },
        ];
    }
}
