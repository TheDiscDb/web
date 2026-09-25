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
    private bool? supportsFileSystemAccess;

    public DiscDirectoryPicker(IJSRuntime js, IFileSystemAccessServiceInProcess fileSystemAccessService)
    {
        this.js = js;
        this.fileSystemAccessService = fileSystemAccessService;
    }

    // Imports the interop module and caches the capability check ahead of time. Call this before
    // the user clicks so that PickAsync performs no awaited `import()` inside the click handler;
    // an awaited import yields the event loop and drops the browser's transient user activation,
    // which makes showDirectoryPicker/input.click silently fail to open in Chromium.
    public async ValueTask PreloadAsync(CancellationToken cancellationToken = default)
    {
        var module = await GetModuleAsync(cancellationToken);
        this.supportsFileSystemAccess ??=
            await module.InvokeAsync<bool>("supportsFileSystemAccess", cancellationToken);
    }

    public async ValueTask<DiscFileSelection?> PickAsync(
        CancellationToken cancellationToken = default,
        Action? onSelectionCommitted = null)
    {
        // Reuse the preloaded module/capability when available so the first awaited call inside the
        // user gesture is the picker itself, preserving transient user activation.
        var module = this.module ?? await GetModuleAsync(cancellationToken);
        var supported = this.supportsFileSystemAccess
            ??= await module.InvokeAsync<bool>("supportsFileSystemAccess", cancellationToken);
        if (supported)
        {
            try
            {
                var root = await this.fileSystemAccessService.ShowDirectoryPickerAsync(
                    new DirectoryPickerOptionsStartInFileSystemHandle
                    {
                        Mode = FileSystemPermissionMode.Read,
                    });

                // The folder is chosen; enumerating its files can be slow on a real disc,
                // so signal "busy" before the enumeration starts.
                onSelectionCommitted?.Invoke();

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

        onSelectionCommitted?.Invoke();

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

    public async ValueTask DownloadAsync(
        string fileName,
        string contentType,
        byte[] contents,
        CancellationToken cancellationToken = default)
    {
        var module = await GetModuleAsync(cancellationToken);
        await module.InvokeVoidAsync(
            "downloadBytes",
            cancellationToken,
            fileName,
            contentType,
            contents);
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

    private static async Task<IReadOnlyList<DiscScanFile>> GetHandleFilesAsync(
        FileSystemDirectoryHandleInProcess root)
    {
        var files = new List<DiscScanFile>();
        await AddFilesAsync(files, root, string.Empty);
        return files;
    }

    private static async Task AddFilesAsync(
        ICollection<DiscScanFile> destination,
        FileSystemDirectoryHandleInProcess directory,
        string path)
    {
        foreach (var handle in await directory.ValuesAsync())
        {
            string relativePath = string.IsNullOrEmpty(path)
                ? handle.Name
                : $"{path}/{handle.Name}";

            if (handle is FileSystemDirectoryHandleInProcess childDirectory)
            {
                await AddFilesAsync(destination, childDirectory, relativePath);
                continue;
            }

            if (handle is not FileSystemFileHandleInProcess fileHandle)
            {
                continue;
            }

            if (destination.Count >= MaxFileCount)
            {
                throw new InvalidDataException(
                    $"The selected directory contains more than {MaxFileCount} files.");
            }

            var file = await fileHandle.GetFileAsync();
            destination.Add(new DiscScanFile(
                relativePath,
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
            : segments.Length == 1 ? 0 : 1;
        return string.Join('/', segments.Skip(startIndex));
    }
}
