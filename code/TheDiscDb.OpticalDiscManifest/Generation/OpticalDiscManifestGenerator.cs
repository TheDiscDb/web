using System.Diagnostics;
using System.Reflection;
using TheDiscDb.OpticalDiscManifest.Models;
using TheDiscDb.OpticalDiscManifest.Serialization;
using TheDiscDb.OpticalDiscManifest.Validation;

namespace TheDiscDb.OpticalDiscManifest.Generation;

public sealed class OpticalDiscManifestGenerator
{
    public const long MaxControlFileSize = 16 * 1024 * 1024;

    public const string SchemaUri =
        "https://schemas.thediscdb.com/optical-disc-manifest/v1/optical-disc-manifest.schema.json";

    private readonly OpticalDiscManifestSchemaValidator validator;

    public OpticalDiscManifestGenerator(OpticalDiscManifestSchemaValidator? validator = null)
    {
        this.validator = validator ?? new OpticalDiscManifestSchemaValidator();
    }

    public async Task<ManifestGenerationResult> GenerateAsync(
        ManifestGenerationRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var stopwatch = Stopwatch.StartNew();
        long memoryBefore = GC.GetTotalMemory(false);
        var diagnostics = new List<ManifestDiagnostic>();
        request.ReportProgress?.Invoke("Normalizing file inventory");
        var files = DiscFileCatalog.NormalizeFiles(request.Files, diagnostics);
        var readMetrics = new ReadMetrics();
        var format = DiscFileCatalog.DetectFormat(files);
        string? discName = null;
        IReadOnlyList<ManifestTitle> titles = Array.Empty<ManifestTitle>();
        IReadOnlyList<ManifestClip> clips = Array.Empty<ManifestClip>();

        if (format == "dvd")
        {
            request.ReportProgress?.Invoke("Parsing DVD navigation metadata");
            titles = await DvdManifestBuilder.ParseDvdAsync(files, diagnostics, readMetrics, cancellationToken);
        }
        else if (format == "blu-ray")
        {
            request.ReportProgress?.Invoke("Parsing Blu-ray navigation metadata");
            var bluRay = await BluRayManifestBuilder.ParseBluRayAsync(files, diagnostics, readMetrics, cancellationToken);
            format = bluRay.IsUhd ? "uhd-blu-ray" : "blu-ray";
            discName = bluRay.DiscName;
            titles = bluRay.Titles;
            clips = bluRay.Clips;
        }
        else
        {
            diagnostics.Add(new ManifestDiagnostic
            {
                Severity = "error",
                Code = "ODM_FORMAT_UNKNOWN",
                Message = "No VIDEO_TS or BDMV control directory was found.",
            });
        }

        request.ReportProgress?.Invoke("Building deterministic manifest");
        var manifest = new OpticalDiscManifestDocument
        {
            Schema = SchemaUri,
            SchemaVersion = 1,
            Producer = new ManifestProducer
            {
                Name = request.ProducerName,
                Version = string.IsNullOrWhiteSpace(request.ProducerVersion)
                    ? DefaultProducerVersion()
                    : request.ProducerVersion,
                Uri = request.ProducerUri,
            },
            Disc = new ManifestDisc
            {
                Format = format,
                Name = discName,
                Identifiers = DiscFileCatalog.CreateIdentifiers(files, format, request.Identifiers),
                Files = files.Select(item => new ManifestFile
                {
                    Path = item.Path,
                    SizeBytes = item.File.Size,
                }).ToArray(),
                Titles = titles.Count > 0 ? titles : null,
                Clips = clips.Count > 0 ? clips : null,
            },
        };

        var json = OpticalDiscManifestJson.Serialize(manifest);
        request.ReportProgress?.Invoke("Validating manifest");
        var validation = validator.Validate(json);
        stopwatch.Stop();

        return new ManifestGenerationResult
        {
            Manifest = manifest,
            Diagnostics = diagnostics
                .OrderBy(item => item.Path, StringComparer.Ordinal)
                .ThenBy(item => item.ByteOffset)
                .ThenBy(item => item.Code, StringComparer.Ordinal)
                .ToArray(),
            Json = json,
            Validation = validation,
            Metrics = new ManifestScanMetrics
            {
                FilesEnumerated = files.Count,
                ControlFilesRead = readMetrics.FilesRead,
                ControlBytesRead = readMetrics.BytesRead,
                OutputBytes = json.LongLength,
                ElapsedMilliseconds = stopwatch.Elapsed.TotalMilliseconds,
                ManagedMemoryBeforeBytes = memoryBefore,
                ManagedMemoryAfterBytes = GC.GetTotalMemory(false),
            },
        };
    }

    private static string DefaultProducerVersion()
        => typeof(OpticalDiscManifestGenerator)
            .Assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?
            .InformationalVersion
            ?? typeof(OpticalDiscManifestGenerator).Assembly.GetName().Version?.ToString()
            ?? "0.0.0";

    public static string NormalizePath(string path)
        => DiscFileCatalog.NormalizePath(path);
}
