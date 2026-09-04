using System.Buffers.Text;
using System.Security.Cryptography;
using System.Text;

namespace TheDiscDb.Core.DiscHash;

/// <summary>
/// Computes disc fingerprints using the Matrix256 version 1 algorithm specified at
/// https://github.com/shitwolfymakes/matrix256/blob/main/SPEC.md.
/// </summary>
public static class DiscFingerprint
{
    public const string Version = "1";

    private static readonly byte[] NullSeparator = [0x00];
    private static readonly byte[] LineFeed = [0x0A];

    public static string Calculate(string root)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(root);

        var entries = new List<DiscFingerprintFile>();
        Scan(new DirectoryInfo(Path.GetFullPath(root)), [], entries);
        return Calculate(entries);
    }

    public static string Calculate(IEnumerable<DiscFingerprintFile> files)
    {
        ArgumentNullException.ThrowIfNull(files);

        var entries = files
            .Select(file => new Entry(CanonicalizePath(file.Path), file.Size))
            .ToList();
        entries.Sort(static (left, right) => left.RelativePath.AsSpan().SequenceCompareTo(right.RelativePath));

        for (int index = 0; index < entries.Count; index++)
        {
            if (entries[index].Size < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(files), "Fingerprint file sizes cannot be negative.");
            }

            if (index > 0 && entries[index - 1].RelativePath.AsSpan().SequenceEqual(entries[index].RelativePath))
            {
                throw new ArgumentException("Fingerprint file paths must be unique after normalization.", nameof(files));
            }
        }

        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        Span<byte> sizeBuffer = stackalloc byte[20];

        foreach (var entry in entries)
        {
            hash.AppendData(entry.RelativePath);
            hash.AppendData(NullSeparator);

            if (!Utf8Formatter.TryFormat(entry.Size, sizeBuffer, out int bytesWritten))
            {
                throw new InvalidOperationException("Unable to serialize a fingerprint file size.");
            }

            hash.AppendData(sizeBuffer[..bytesWritten]);
            hash.AppendData(LineFeed);
        }

        return Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant();
    }

    public static bool IsValidFingerprint(string? value)
        => value is { Length: 64 } && value.All(static character =>
            character is >= '0' and <= '9' or >= 'a' and <= 'f');

    private static void Scan(
        DirectoryInfo directory,
        List<string> ancestors,
        List<DiscFingerprintFile> entries)
    {
        foreach (FileSystemInfo item in directory.EnumerateFileSystemInfos())
        {
            if (item.LinkTarget is not null)
            {
                continue;
            }

            var components = new List<string>(ancestors.Count + 1);
            components.AddRange(ancestors);
            components.Add(item.Name);

            if ((item.Attributes & FileAttributes.Directory) != 0)
            {
                Scan((DirectoryInfo)item, components, entries);
                continue;
            }

            if (item is not FileInfo file || (item.Attributes & FileAttributes.Device) != 0)
            {
                continue;
            }

            entries.Add(new DiscFingerprintFile(string.Join('/', components), file.Length));
        }
    }

    private static byte[] CanonicalizePath(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        string relativePath = path.Replace('\\', '/');
        if (relativePath.StartsWith('/') || Path.IsPathRooted(relativePath))
        {
            throw new ArgumentException("Fingerprint paths must be relative.", nameof(path));
        }

        var components = relativePath.Split('/');
        if (components.Any(static component => component.Length == 0 || component is "." or ".."))
        {
            throw new ArgumentException(
                "Fingerprint paths cannot contain empty, current-directory, or parent-directory components.",
                nameof(path));
        }

        return Encoding.UTF8.GetBytes(ReplaceInvalidSurrogates(relativePath).Normalize(NormalizationForm.FormC));
    }

    private static string ReplaceInvalidSurrogates(string value)
    {
        StringBuilder? result = null;

        for (int index = 0; index < value.Length; index++)
        {
            char current = value[index];
            if (!char.IsSurrogate(current))
            {
                result?.Append(current);
                continue;
            }

            if (char.IsHighSurrogate(current)
                && index + 1 < value.Length
                && char.IsLowSurrogate(value[index + 1]))
            {
                if (result is not null)
                {
                    result.Append(current);
                    result.Append(value[++index]);
                }
                else
                {
                    index++;
                }

                continue;
            }

            result ??= new StringBuilder(value.AsSpan(0, index).ToString());
            result.Append('\uFFFD');
        }

        return result?.ToString() ?? value;
    }

    private sealed record Entry(byte[] RelativePath, long Size);
}

public sealed record DiscFingerprintFile(string Path, long Size);
