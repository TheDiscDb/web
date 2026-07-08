using System.Text.RegularExpressions;
using KristofferStrube.Blazor.FileAPI;
using KristofferStrube.Blazor.FileSystem;
using TheDiscDb.Core.DiscHash;

namespace TheDiscDb.Client.Pages.Contribute;

/// <summary>Outcome of scanning a picked disc/backup root folder.</summary>
public sealed record DiscScanResult(
    string? Format,
    string? GlobalDiscId,
    IReadOnlyList<FileHashInfo> HashFiles,
    string? Error);

/// <summary>
/// Shared client-side disc scanning: given a picked disc-root directory handle, gathers the
/// content-hash file metadata (exactly as <c>AddDisc</c> does, so the server-side content-hash
/// matches) and computes the <c>GlobalDiscId</c> (AACS SHA-1 for Blu-ray/UHD, libdvdread MD5 for
/// DVD). All bytes are read and hashed in the browser; only metadata + the hex Disc ID leave.
/// </summary>
public static class DiscScanner
{
    private static readonly Regex VtsIfoPattern =
        new(@"^VTS_(\d{2})_0\.IFO$", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public static async Task<DiscScanResult> ScanAsync(FileSystemDirectoryHandleInProcess root)
    {
        var rootItems = await root.ValuesAsync();
        var bdmv = FindDirectory(rootItems, "BDMV");
        var videoTs = FindDirectory(rootItems, "VIDEO_TS");

        if (bdmv is not null)
        {
            var bdmvItems = await bdmv.ValuesAsync();
            var stream = FindDirectory(bdmvItems, "STREAM");
            if (stream is null)
            {
                return new DiscScanResult(null, null, Array.Empty<FileHashInfo>(), "No BDMV/STREAM folder found.");
            }

            var hashFiles = await GetHashItems(stream, i => i.Name.EndsWith("m2ts", StringComparison.OrdinalIgnoreCase));
            var globalDiscId = await TryComputeAacsDiscId(rootItems);
            return new DiscScanResult("Blu-ray", globalDiscId, hashFiles, null);
        }

        if (videoTs is not null)
        {
            var hashFiles = await GetHashItems(videoTs, filter: null);
            var globalDiscId = await TryComputeDvdDiscId(videoTs);
            return new DiscScanResult("DVD", globalDiscId, hashFiles, null);
        }

        return new DiscScanResult(null, null, Array.Empty<FileHashInfo>(),
            "No BDMV or VIDEO_TS folder found. Pick the disc root (the folder containing BDMV/AACS or VIDEO_TS).");
    }

    private static async Task<List<FileHashInfo>> GetHashItems(FileSystemDirectoryHandleInProcess root, Predicate<FileInProcess>? filter)
    {
        var results = new List<FileHashInfo>();
        var items = await root.ValuesAsync();
        int index = 0;
        foreach (var item in items.Where(i => i.Kind == FileSystemHandleKind.File))
        {
            if (item is not FileSystemFileHandleInProcess file)
            {
                continue;
            }

            var fileData = await file.GetFileAsync();
            if (filter != null && !filter(fileData))
            {
                continue;
            }

            results.Add(new FileHashInfo
            {
                Index = index++,
                Name = file.Name,
                Size = (long)fileData.Size,
                CreationTime = fileData.LastModified,
            });
        }

        return results;
    }

    private static async Task<string?> TryComputeAacsDiscId(IFileSystemHandleInProcess[] rootItems)
    {
        try
        {
            var aacs = FindDirectory(rootItems, "AACS");
            if (aacs is null)
            {
                return null;
            }

            var aacsItems = await aacs.ValuesAsync();
            var unitKey = FindFile(aacsItems, "Unit_Key_RO.inf");
            if (unitKey is null)
            {
                var duplicate = FindDirectory(aacsItems, "DUPLICATE");
                if (duplicate is not null)
                {
                    unitKey = FindFile(await duplicate.ValuesAsync(), "Unit_Key_RO.inf");
                }
            }

            if (unitKey is null)
            {
                return null;
            }

            return AacsDiscId.Compute(await ReadBytes(unitKey));
        }
        catch (Exception ex)
        {
            Console.WriteLine($"AACS Disc ID computation skipped: {ex.Message}");
            return null;
        }
    }

    private static async Task<string?> TryComputeDvdDiscId(FileSystemDirectoryHandleInProcess videoTs)
    {
        try
        {
            var items = await videoTs.ValuesAsync();
            var videoTsIfoHandle = FindFile(items, "VIDEO_TS.IFO");
            if (videoTsIfoHandle is null)
            {
                return null;
            }

            var videoTsIfo = await ReadBytes(videoTsIfoHandle);

            var vtsByNumber = new SortedDictionary<int, byte[]>();
            foreach (var item in items)
            {
                if (item.Kind != FileSystemHandleKind.File)
                {
                    continue;
                }

                var match = VtsIfoPattern.Match(item.Name);
                if (match.Success && item is FileSystemFileHandleInProcess fileHandle)
                {
                    vtsByNumber[int.Parse(match.Groups[1].Value)] = await ReadBytes(fileHandle);
                }
            }

            int maxNumber = vtsByNumber.Count > 0 ? vtsByNumber.Keys.Max() : 0;
            var vtsIfos = new byte[]?[maxNumber];
            foreach (var kvp in vtsByNumber)
            {
                vtsIfos[kvp.Key - 1] = kvp.Value;
            }

            return DvdDiscId.Compute(videoTsIfo, vtsIfos);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"DVD Disc ID computation skipped: {ex.Message}");
            return null;
        }
    }

    private static FileSystemDirectoryHandleInProcess? FindDirectory(IEnumerable<IFileSystemHandleInProcess> items, string name)
        => items.FirstOrDefault(i => i.Kind == FileSystemHandleKind.Directory && string.Equals(i.Name, name, StringComparison.OrdinalIgnoreCase))
            as FileSystemDirectoryHandleInProcess;

    private static FileSystemFileHandleInProcess? FindFile(IEnumerable<IFileSystemHandleInProcess> items, string name)
        => items.FirstOrDefault(i => i.Kind == FileSystemHandleKind.File && string.Equals(i.Name, name, StringComparison.OrdinalIgnoreCase))
            as FileSystemFileHandleInProcess;

    private static async Task<byte[]> ReadBytes(FileSystemFileHandleInProcess handle)
    {
        var file = await handle.GetFileAsync();
        return await file.ArrayBufferAsync();
    }
}
