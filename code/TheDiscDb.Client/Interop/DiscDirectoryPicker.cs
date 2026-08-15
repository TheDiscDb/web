using KristofferStrube.Blazor.FileAPI;
using KristofferStrube.Blazor.FileSystem;
using KristofferStrube.Blazor.FileSystemAccess;
using Microsoft.JSInterop;
using TheDiscDb.Client.Pages.Contribute;

namespace TheDiscDb.Client.Interop;

public sealed class DiscDirectoryPicker : IAsyncDisposable
{
    private const int MaxFileCount = 100_000;
    private readonly IJSRuntime js;
    private readonly IFileSystemAccessServiceInProcess fileSystemAccessService;
    private IJSObjectReference? module;

    public DiscDirectoryPicker(IJSRuntime js, IFileSystemAccessServiceInProcess fileSystemAccessService)
    {
        this.js = js;
        this.fileSystemAccessService = fileSystemAccessService;
    }

    public async ValueTask<DiscFileSelection?> PickAsync(CancellationToken cancellationToken = default)
    {
        var module = await GetModuleAsync(cancellationToken);
        if (await module.InvokeAsync<bool>("supportsFileSystemAccess", cancellationToken))
        {
            try
            {
                var root = await this.fileSystemAccessService.ShowDirectoryPickerAsync(
                    new DirectoryPickerOptionsStartInFileSystemHandle
                    {
                        Mode = FileSystemPermissionMode.Read,
                    });

                return new DiscFileSelection(await GetHandleFilesAsync(root));
            }
            catch (JSException ex) when (IsCancellation(ex))
            {
                return null;
            }
        }

        var result = await module.InvokeAsync<DirectorySelectionResult?>("pickDirectory", cancellationToken, MaxFileCount);
        if (result is null)
        {
            return null;
        }

        DiscScanFile[] files;
        try
        {
            files = result.Files.Select(file => new DiscScanFile(
                DiscPath.NormalizeDirectoryUploadPath(file.RelativePath),
                file.Name,
                file.Size,
                DateTimeOffset.FromUnixTimeMilliseconds(file.LastModified).UtcDateTime,
                (maxAllowedSize, token) => module.InvokeAsync<byte[]>(
                    "readFile",
                    token,
                    result.SelectionId,
                    file.Id,
                    maxAllowedSize))).ToArray();
        }
        catch
        {
            await module.InvokeVoidAsync("releaseSelection", result.SelectionId);
            throw;
        }

        return new DiscFileSelection(files, () => module.InvokeVoidAsync("releaseSelection", result.SelectionId));
    }

    public async ValueTask DisposeAsync()
    {
        if (this.module is not null)
        {
            await this.module.DisposeAsync();
        }
    }

    private async ValueTask<IJSObjectReference> GetModuleAsync(CancellationToken cancellationToken)
    {
        this.module ??= await this.js.InvokeAsync<IJSObjectReference>(
            "import",
            cancellationToken,
            "/disc-directory-picker.js");
        return this.module;
    }

    private static bool IsCancellation(JSException exception)
        => exception.Message.Contains("AbortError", StringComparison.OrdinalIgnoreCase)
            || exception.Message.Contains("cancel", StringComparison.OrdinalIgnoreCase);

    private static async Task<IReadOnlyList<DiscScanFile>> GetHandleFilesAsync(FileSystemDirectoryHandleInProcess root)
    {
        var files = new List<DiscScanFile>();
        var rootItems = await root.ValuesAsync();

        if (FindDirectory(rootItems, "BDMV") is { } bdmv
            && FindDirectory(await bdmv.ValuesAsync(), "STREAM") is { } stream)
        {
            await AddFilesAsync(files, stream, "BDMV/STREAM");
        }

        if (FindDirectory(rootItems, "AACS") is { } aacs)
        {
            await AddFilesAsync(files, aacs, "AACS");
            if (FindDirectory(await aacs.ValuesAsync(), "DUPLICATE") is { } duplicate)
            {
                await AddFilesAsync(files, duplicate, "AACS/DUPLICATE");
            }
        }

        if (FindDirectory(rootItems, "VIDEO_TS") is { } videoTs)
        {
            await AddFilesAsync(files, videoTs, "VIDEO_TS");
        }

        return files;
    }

    private static async Task AddFilesAsync(ICollection<DiscScanFile> destination, FileSystemDirectoryHandleInProcess directory, string path)
    {
        foreach (var handle in await directory.ValuesAsync())
        {
            if (handle is not FileSystemFileHandleInProcess fileHandle)
            {
                continue;
            }

            var file = await fileHandle.GetFileAsync();
            destination.Add(new DiscScanFile(
                $"{path}/{fileHandle.Name}",
                fileHandle.Name,
                (long)file.Size,
                file.LastModified,
                async (maxAllowedSize, _) =>
                {
                    var selectedFile = await fileHandle.GetFileAsync();
                    if ((long)selectedFile.Size > maxAllowedSize)
                    {
                        throw new IOException(
                            $"{fileHandle.Name} exceeds the {maxAllowedSize}-byte read limit.");
                    }

                    return await selectedFile.ArrayBufferAsync();
                }));
        }
    }

    private static FileSystemDirectoryHandleInProcess? FindDirectory(
        IEnumerable<IFileSystemHandleInProcess> items,
        string name)
        => items.FirstOrDefault(item =>
            item.Kind == FileSystemHandleKind.Directory
            && string.Equals(item.Name, name, StringComparison.OrdinalIgnoreCase))
            as FileSystemDirectoryHandleInProcess;

    private sealed record DirectorySelectionResult(
        string SelectionId,
        IReadOnlyList<DirectorySelectionFile> Files);

    private sealed record DirectorySelectionFile(
        string Id,
        string Name,
        string RelativePath,
        long Size,
        long LastModified);
}

public static class DiscPath
{
    public static string NormalizeDirectoryUploadPath(string relativePath)
    {
        var normalized = relativePath.Replace('\\', '/').Trim('/');
        var segments = normalized.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (segments.Length == 0 || segments.Any(segment => segment is "." or ".."))
        {
            throw new InvalidDataException(
                $"The selected directory returned an invalid file path: {relativePath}");
        }

        int discRootIndex = Array.FindIndex(
            segments,
            segment => segment.Equals("BDMV", StringComparison.OrdinalIgnoreCase)
                || segment.Equals("AACS", StringComparison.OrdinalIgnoreCase)
                || segment.Equals("VIDEO_TS", StringComparison.OrdinalIgnoreCase));

        int startIndex = discRootIndex >= 0
            ? discRootIndex
            : segments.Length > 1 ? 1 : 0;
        return string.Join('/', segments.Skip(startIndex));
    }
}
