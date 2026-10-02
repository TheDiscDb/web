using TheDiscDb.OpticalDiscManifest.Models;
using TheDiscDb.OpticalDiscParsers.Diagnostics;
using TheDiscDb.OpticalDiscParsers.Input;
using TheDiscDb.OpticalDiscParsers.Models;

namespace TheDiscDb.OpticalDiscManifest.Generation;

internal static class ControlFileReader
{
    internal static async Task<ParsedControl<T>?> ParseControlWithBackupAsync<T>(
        DiscFileCatalog files,
        string primaryPath,
        string backupPath,
        ICollection<ManifestDiagnostic> diagnostics,
        ReadMetrics metrics,
        CancellationToken cancellationToken,
        Func<byte[], Task<ParserResult<T>>> parseAsync)
        where T : class
    {
        var primary = DiscFileCatalog.Find(files, primaryPath);
        var backup = DiscFileCatalog.Find(files, backupPath);
        var parsed = primary is not null
            ? await TryParseControlAsync(primary, diagnostics, metrics, cancellationToken, parseAsync)
            : null;
        if (parsed?.Result.IsSuccessful == true || backup is null)
        {
            return parsed;
        }

        var backupParsed = await TryParseControlAsync(backup, diagnostics, metrics, cancellationToken, parseAsync);
        if (backupParsed?.Value is null)
        {
            return parsed ?? backupParsed;
        }

        diagnostics.Add(new ManifestDiagnostic
        {
            Severity = "info",
            Code = "ODM_CONTROL_FILE_BACKUP_USED",
            Message = primary is null
                ? $"Used backup control file because {primaryPath} was missing."
                : $"Used backup control file because {primaryPath} could not be parsed completely.",
            Path = backup.Path,
        });

        return backupParsed;
    }

    private static async Task<ParsedControl<T>?> TryParseControlAsync<T>(
        NormalizedFile file,
        ICollection<ManifestDiagnostic> diagnostics,
        ReadMetrics metrics,
        CancellationToken cancellationToken,
        Func<byte[], Task<ParserResult<T>>> parseAsync)
        where T : class
    {
        var bytes = await ReadControlFileAsync(file, diagnostics, metrics, cancellationToken);
        if (bytes is null)
        {
            return null;
        }

        var result = await parseAsync(bytes);
        AddDiagnostics(diagnostics, file.Path, result.Diagnostics);
        return new ParsedControl<T>(file.Path, result.Value, result);
    }

    internal static async Task<byte[]?> ReadControlFileAsync(
        NormalizedFile file,
        ICollection<ManifestDiagnostic> diagnostics,
        ReadMetrics metrics,
        CancellationToken cancellationToken)
    {
        if (file.File.Size < 0 || file.File.Size > OpticalDiscManifestGenerator.MaxControlFileSize)
        {
            diagnostics.Add(new ManifestDiagnostic
            {
                Severity = "error",
                Code = "ODM_CONTROL_FILE_SIZE",
                Message = $"Control file size {file.File.Size} exceeds the {OpticalDiscManifestGenerator.MaxControlFileSize}-byte limit.",
                Path = file.Path,
            });
            return null;
        }

        try
        {
            var bytes = await file.File.ReadBytesAsync(OpticalDiscManifestGenerator.MaxControlFileSize, cancellationToken);
            metrics.FilesRead++;
            metrics.BytesRead += bytes.LongLength;
            if (bytes.LongLength != file.File.Size)
            {
                diagnostics.Add(new ManifestDiagnostic
                {
                    Severity = "warning",
                    Code = "ODM_CONTROL_FILE_TRUNCATED",
                    Message = $"Expected {file.File.Size} bytes but read {bytes.LongLength}.",
                    Path = file.Path,
                });
            }

            return bytes;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            diagnostics.Add(new ManifestDiagnostic
            {
                Severity = "error",
                Code = "ODM_CONTROL_FILE_READ",
                Message = ex.Message,
                Path = file.Path,
            });
            return null;
        }
    }

    private static void AddDiagnostics(
        ICollection<ManifestDiagnostic> destination,
        string path,
        IEnumerable<ParserDiagnostic> source)
    {
        foreach (var diagnostic in source)
        {
            destination.Add(new ManifestDiagnostic
            {
                Severity = diagnostic.Severity.ToString().ToLowerInvariant(),
                Code = diagnostic.Code,
                Message = diagnostic.Message,
                Path = path,
                ByteOffset = diagnostic.Offset,
            });
        }
    }

    internal static IOpticalDiscReader CreateReader(byte[] bytes)
        => new MemoryOpticalDiscReader(bytes);

    internal static ManifestDiagnostic MissingFile(string path)
        => new()
        {
            Severity = "warning",
            Code = "ODM_CONTROL_FILE_MISSING",
            Message = "Expected control metadata was not found.",
            Path = path,
        };

    internal sealed record ParsedControl<T>(string Path, T? Value, ParserResult<T> Result) where T : class;
}
