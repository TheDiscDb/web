using System.Buffers.Binary;
using System.Collections;
using TheDiscDb.Core.DiscHash;
using TheDiscDb.OpticalDiscManifest.Models;

namespace TheDiscDb.OpticalDiscManifest.Generation;

internal sealed class DiscFileCatalog : IReadOnlyList<NormalizedFile>
{
    private readonly IReadOnlyList<NormalizedFile> files;
    private readonly IReadOnlyDictionary<string, NormalizedFile> byPath;

    private DiscFileCatalog(IReadOnlyList<NormalizedFile> files)
    {
        this.files = files;
        byPath = files.ToDictionary(file => file.Path, StringComparer.OrdinalIgnoreCase);
    }

    public int Count => files.Count;

    public NormalizedFile this[int index] => files[index];

    public IEnumerator<NormalizedFile> GetEnumerator() => files.GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    internal int EnumerationIndexOf(string? path)
        => path is not null && byPath.TryGetValue(path, out var file) ? file.EnumerationIndex : int.MaxValue;

    internal static string NormalizePath(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        var normalized = path.Replace('\\', '/').Trim('/');
        var segments = normalized.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (segments.Length == 0 || segments.Any(segment => segment is "." or ".."))
        {
            throw new InvalidDataException($"Invalid disc-relative path: {path}");
        }

        return string.Join('/', segments);
    }

    internal static DiscFileCatalog NormalizeFiles(
        IReadOnlyList<IManifestDiscFile> source,
        ICollection<ManifestDiagnostic> diagnostics)
    {
        var files = new List<NormalizedFile>(source.Count);
        var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var file in source)
        {
            string path;
            try
            {
                path = NormalizePath(file.Path);
            }
            catch (Exception ex) when (ex is ArgumentException or InvalidDataException)
            {
                diagnostics.Add(new ManifestDiagnostic
                {
                    Severity = "error",
                    Code = "ODM_PATH_INVALID",
                    Message = ex.Message,
                });
                continue;
            }

            if (!paths.Add(path))
            {
                diagnostics.Add(new ManifestDiagnostic
                {
                    Severity = "error",
                    Code = "ODM_PATH_DUPLICATE",
                    Message = "The selected directory contains duplicate case-insensitive paths.",
                    Path = path,
                });
                continue;
            }

            files.Add(new NormalizedFile(path, file, files.Count));
        }

        return new DiscFileCatalog(files.OrderBy(item => item.Path, StringComparer.Ordinal).ToArray());
    }

    internal static string DetectFormat(IReadOnlyList<NormalizedFile> files)
    {
        bool dvd = files.Any(item =>
            item.Path.StartsWith("VIDEO_TS/", StringComparison.OrdinalIgnoreCase));
        bool bluRay = files.Any(item =>
            item.Path.StartsWith("BDMV/", StringComparison.OrdinalIgnoreCase));

        return (dvd, bluRay) switch
        {
            (true, false) => "dvd",
            (false, true) => "blu-ray",
            _ => "unknown",
        };
    }

    internal static IReadOnlyList<ManifestIdentifier> CreateIdentifiers(
        IReadOnlyList<NormalizedFile> files,
        string format,
        IReadOnlyList<ManifestIdentifier>? suppliedIdentifiers)
    {
        var identifiers = (suppliedIdentifiers ?? [])
            .Where(item => !string.Equals(item.Kind, "thediscdb-content-hash", StringComparison.Ordinal))
            .Append(new ManifestIdentifier
            {
                Kind = "thediscdb-content-hash",
                Value = ComputeContentHash(files, format),
            })
            .OrderBy(item => item.Kind, StringComparer.Ordinal)
            .ToArray();

        return identifiers;
    }

    private static string ComputeContentHash(IReadOnlyList<NormalizedFile> files, string format)
    {
        var payloadFiles = files
            .Where(item => IsContentHashInput(item.Path, format))
            .OrderBy(item => Path.GetFileName(item.Path), StringComparer.Ordinal);

        // System.Security.Cryptography.MD5 throws Cryptography_UnknownHashAlgorithm in
        // Blazor WebAssembly, where the crypto backend only exposes SubtleCrypto's SHA family.
        var hash = new Md5Digest();
        Span<byte> sizeBytes = stackalloc byte[sizeof(long)];
        foreach (var file in payloadFiles)
        {
            if (file.File.Size < 0)
            {
                throw new InvalidDataException($"File '{file.Path}' has a negative size and cannot be hashed.");
            }

            BinaryPrimitives.WriteInt64LittleEndian(sizeBytes, file.File.Size);
            hash.Append(sizeBytes);
        }

        return Convert.ToHexString(hash.Finish());
    }

    private static bool IsContentHashInput(string path, string format)
    {
        if (path.StartsWith("BDMV/BACKUP/", StringComparison.OrdinalIgnoreCase)
            || path.EndsWith(".ssif", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return format switch
        {
            "blu-ray" or "uhd-blu-ray" => IsControlFile(path, "BDMV/STREAM", ".m2ts"),
            "dvd" => path.StartsWith("VIDEO_TS/", StringComparison.OrdinalIgnoreCase)
                && (path.EndsWith(".vob", StringComparison.OrdinalIgnoreCase)
                    || path.EndsWith(".ifo", StringComparison.OrdinalIgnoreCase)
                    || path.EndsWith(".bup", StringComparison.OrdinalIgnoreCase)),
            _ => false,
        };
    }

    internal static NormalizedFile? Find(DiscFileCatalog files, string path)
        => files.byPath.TryGetValue(path, out var file) ? file : null;

    internal static bool IsControlFile(string path, string directory, string extension)
        => path.StartsWith($"{directory}/", StringComparison.OrdinalIgnoreCase)
            && path.EndsWith(extension, StringComparison.OrdinalIgnoreCase)
            && path[(directory.Length + 1)..].IndexOf('/') < 0;
}
