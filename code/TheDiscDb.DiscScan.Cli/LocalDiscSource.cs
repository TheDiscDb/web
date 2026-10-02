using TheDiscDb.OpticalDiscManifest.Generation;

namespace TheDiscDb.DiscScan.Cli;

public sealed class LocalDiscSource
{
    private static readonly EnumerationOptions RecursiveEnumeration = new()
    {
        RecurseSubdirectories = true,
        IgnoreInaccessible = true,
        AttributesToSkip = FileAttributes.System,
    };

    private LocalDiscSource(
        string rootPath,
        string displayPath,
        string label,
        string format,
        IReadOnlyList<IManifestDiscFile> files,
        IReadOnlyList<string> warnings)
    {
        RootPath = rootPath;
        DisplayPath = displayPath;
        Label = label;
        Format = format;
        Files = files;
        Warnings = warnings;
    }

    public string RootPath { get; }

    public string DisplayPath { get; }

    public string Label { get; }

    public string Format { get; }

    public IReadOnlyList<IManifestDiscFile> Files { get; }

    public IReadOnlyList<string> Warnings { get; }

    public static bool TryOpen(string path, out LocalDiscSource? source, out string error)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        source = null;
        error = string.Empty;
        string resolvedPath = NormalizeInputPath(path);
        if (!Directory.Exists(resolvedPath))
        {
            error = $"Path does not exist or is not a directory: {path}";
            return false;
        }

        string? discRoot = TryFindDiscRoot(resolvedPath);
        if (discRoot is null)
        {
            error = $"No VIDEO_TS or BDMV folder was found under: {resolvedPath}";
            return false;
        }

        var warnings = new List<string>();
        var files = EnumerateFiles(discRoot, warnings);
        string format = DetectFormat(files);
        if (format == "unknown")
        {
            error = $"No readable VIDEO_TS or BDMV files were found under: {discRoot}";
            return false;
        }

        source = new LocalDiscSource(
            discRoot,
            TrimTrailingDirectorySeparator(discRoot),
            GetVolumeLabel(discRoot),
            format,
            files,
            warnings);
        return true;
    }

    public static IReadOnlyList<LocalDiscCandidate> ListCandidates()
    {
        var candidates = new Dictionary<string, LocalDiscCandidate>(PathComparer);
        foreach (string path in EnumerateCandidateRoots())
        {
            if (!Directory.Exists(path))
            {
                continue;
            }

            string normalized = NormalizeInputPath(path);
            if (!candidates.ContainsKey(normalized)
                && TryOpen(normalized, out LocalDiscSource? source, out _)
                && source is not null)
            {
                candidates.Add(
                    normalized,
                    new LocalDiscCandidate(source.DisplayPath, source.Label, source.Format));
            }
        }

        return candidates.Values
            .OrderBy(item => item.Path, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    internal static IReadOnlyList<string> EnumerateCandidateRootsForTest(
        string? operatingSystem = null,
        IEnumerable<DriveInfo>? drives = null,
        Func<string, bool>? directoryExists = null,
        Func<string, IEnumerable<string>>? enumerateDirectories = null)
        => EnumerateCandidateRoots(
            operatingSystem ?? OperatingSystemName(),
            drives ?? DriveInfo.GetDrives(),
            directoryExists ?? Directory.Exists,
            enumerateDirectories ?? Directory.EnumerateDirectories).ToArray();

    private static IReadOnlyList<IManifestDiscFile> EnumerateFiles(
        string root,
        ICollection<string> warnings)
    {
        var files = new List<IManifestDiscFile>();
        try
        {
            foreach (string path in Directory.EnumerateFiles(root, "*", RecursiveEnumeration))
            {
                if (IsIgnoredPath(path))
                {
                    continue;
                }

                try
                {
                    files.Add(new LocalDiscFile(
                        OpticalDiscManifestGenerator.NormalizePath(Path.GetRelativePath(root, path)),
                        path));
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or InvalidDataException)
                {
                    warnings.Add($"{path}: {ex.Message}");
                }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            warnings.Add($"{root}: {ex.Message}");
        }

        return files;
    }

    private static string DetectFormat(IReadOnlyList<IManifestDiscFile> files)
    {
        bool dvd = files.Any(file => file.Path.StartsWith("VIDEO_TS/", StringComparison.OrdinalIgnoreCase));
        bool bluRay = files.Any(file => file.Path.StartsWith("BDMV/", StringComparison.OrdinalIgnoreCase));
        return (dvd, bluRay) switch
        {
            (true, false) => "dvd",
            (false, true) => "blu-ray",
            (false, false) => "unknown",
            _ => "unknown",
        };
    }

    private static string? TryFindDiscRoot(string path)
    {
        if (ContainsDiscDirectory(path))
        {
            return path;
        }

        string videoTs = Path.GetFileName(path).Equals("VIDEO_TS", StringComparison.OrdinalIgnoreCase)
            ? Directory.GetParent(path)?.FullName ?? path
            : path;
        if (ContainsDiscDirectory(videoTs))
        {
            return videoTs;
        }

        string bdmv = Path.GetFileName(path).Equals("BDMV", StringComparison.OrdinalIgnoreCase)
            ? Directory.GetParent(path)?.FullName ?? path
            : path;
        return ContainsDiscDirectory(bdmv) ? bdmv : null;
    }

    private static bool ContainsDiscDirectory(string path)
        => Directory.EnumerateDirectories(path)
            .Any(item =>
            {
                string name = Path.GetFileName(item);
                return name.Equals("VIDEO_TS", StringComparison.OrdinalIgnoreCase)
                    || name.Equals("BDMV", StringComparison.OrdinalIgnoreCase);
            });

    private static IEnumerable<string> EnumerateCandidateRoots()
        => EnumerateCandidateRoots(
            OperatingSystemName(),
            DriveInfo.GetDrives(),
            Directory.Exists,
            Directory.EnumerateDirectories);

    private static IEnumerable<string> EnumerateCandidateRoots(
        string operatingSystem,
        IEnumerable<DriveInfo> drives,
        Func<string, bool> directoryExists,
        Func<string, IEnumerable<string>> enumerateDirectories)
    {
        foreach (DriveInfo drive in drives)
        {
            if (!drive.IsReady)
            {
                continue;
            }

            if (drive.DriveType is DriveType.CDRom or DriveType.Removable or DriveType.Fixed)
            {
                yield return drive.RootDirectory.FullName;
            }
        }

        if (operatingSystem == "macos")
        {
            foreach (string path in EnumerateChildren("/Volumes", directoryExists, enumerateDirectories))
            {
                yield return path;
            }
        }
        else if (operatingSystem == "linux")
        {
            foreach (string path in EnumerateChildren("/media", directoryExists, enumerateDirectories))
            {
                yield return path;
            }

            foreach (string userDirectory in EnumerateChildren("/media", directoryExists, enumerateDirectories))
            {
                foreach (string path in EnumerateChildren(userDirectory, directoryExists, enumerateDirectories))
                {
                    yield return path;
                }
            }

            foreach (string userDirectory in EnumerateChildren("/run/media", directoryExists, enumerateDirectories))
            {
                foreach (string path in EnumerateChildren(userDirectory, directoryExists, enumerateDirectories))
                {
                    yield return path;
                }
            }

            foreach (string path in EnumerateChildren("/mnt", directoryExists, enumerateDirectories))
            {
                yield return path;
            }
        }
    }

    private static IEnumerable<string> EnumerateChildren(
        string path,
        Func<string, bool> directoryExists,
        Func<string, IEnumerable<string>> enumerateDirectories)
    {
        if (!directoryExists(path))
        {
            yield break;
        }

        IEnumerable<string> children;
        try
        {
            children = enumerateDirectories(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            yield break;
        }

        foreach (string child in children)
        {
            yield return child;
        }
    }

    private static string NormalizeInputPath(string path)
    {
        if (OperatingSystem.IsWindows()
            && path.Length == 2
            && char.IsAsciiLetter(path[0])
            && path[1] == ':')
        {
            path += Path.DirectorySeparatorChar;
        }

        return Path.GetFullPath(path);
    }

    private static string GetVolumeLabel(string root)
    {
        string trimmedRoot = TrimTrailingDirectorySeparator(root);
        string fullRoot = TrimTrailingDirectorySeparator(Path.GetPathRoot(root) ?? root);
        if (PathComparer.Equals(trimmedRoot, fullRoot))
        {
            try
            {
                var drive = new DriveInfo(fullRoot);
                if (!string.IsNullOrWhiteSpace(drive.VolumeLabel))
                {
                    return drive.VolumeLabel;
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
            {
            }
        }

        string name = Path.GetFileName(trimmedRoot);
        return string.IsNullOrWhiteSpace(name) ? "disc" : name;
    }

    private static string TrimTrailingDirectorySeparator(string path)
        => path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

    private static bool IsIgnoredPath(string path)
        => path.Contains($"{Path.DirectorySeparatorChar}System Volume Information{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase)
            || path.Contains($"{Path.DirectorySeparatorChar}$RECYCLE.BIN{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase);

    private static StringComparer PathComparer
        => OperatingSystem.IsWindows() || OperatingSystem.IsMacOS()
            ? StringComparer.OrdinalIgnoreCase
            : StringComparer.Ordinal;

    private static string OperatingSystemName()
    {
        if (OperatingSystem.IsWindows())
        {
            return "windows";
        }

        if (OperatingSystem.IsMacOS())
        {
            return "macos";
        }

        return "linux";
    }

    private sealed class LocalDiscFile : IManifestDiscFile
    {
        private readonly string fullPath;

        public LocalDiscFile(string path, string fullPath)
        {
            Path = path.Replace('\\', '/');
            this.fullPath = fullPath;
            Size = new FileInfo(fullPath).Length;
        }

        public string Path { get; }

        public long Size { get; }

        public async ValueTask<byte[]> ReadBytesAsync(
            long maxAllowedSize,
            CancellationToken cancellationToken = default)
        {
            if (Size > maxAllowedSize)
            {
                throw new IOException($"{Path} exceeds the {maxAllowedSize}-byte read limit.");
            }

            return await File.ReadAllBytesAsync(fullPath, cancellationToken);
        }
    }
}

public sealed record LocalDiscCandidate(string Path, string Label, string Format);
