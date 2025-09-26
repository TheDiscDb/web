using KristofferStrube.Blazor.FileSystem;
using KristofferStrube.Blazor.FileSystemAccess;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;
using TheDiscDb.Core.DiscHash;

namespace TheDiscDb.Client.Pages.Contribute;

[Authorize]
public partial class CalculateHash : ComponentBase
{
    [Inject]
    public IFileSystemAccessServiceInProcess FileSystemAccessService { get; set; } = default!;

    [Inject]
    public IJSRuntime Js { get; set; } = default!;

    [Inject]
    public HashClient HashClient { get; set; } = default!;

    [Inject]
    public NavigationManager Navigation { get; set; } = default!;

    FileSystemDirectoryHandleInProcess? handler;
    IFileSystemHandleInProcess[] items = Array.Empty<IFileSystemHandleInProcess>();
    string hash = string.Empty;

    async Task OpenFolderAsync()
    {
        try
        {
            handler = await FileSystemAccessService.ShowDirectoryPickerAsync(new DirectoryPickerOptionsStartInFileSystemHandle()
            {
                Mode = FileSystemPermissionMode.Read,
            });

            await TryCalculateHash();
        }
        catch (Exception ex)
        {
            Console.WriteLine(ex);
        }
    }

    async Task TryCalculateHash()
    {
        items = await handler!.ValuesAsync();

        var bdmv = items.FirstOrDefault(i => i.Kind == FileSystemHandleKind.Directory && i.Name.Equals("BDMV", StringComparison.OrdinalIgnoreCase));
        if (bdmv != default && bdmv is FileSystemDirectoryHandleInProcess bdmvDirectory)
        {
            items = await bdmvDirectory.ValuesAsync();
            var stream = items.FirstOrDefault(i => i.Kind == FileSystemHandleKind.Directory && i.Name.Equals("STREAM", StringComparison.OrdinalIgnoreCase));
            if (stream != default && stream is FileSystemDirectoryHandleInProcess streamDirectory)
            {
                var hashItems = await HashBluray(streamDirectory);

                var request = new HashRequest
                {
                    Files = hashItems.ToList()
                };

                // TODO: Get a cancellation token
                var response = await this.HashClient.HashAsync(request);
                if (response != null && !string.IsNullOrEmpty(response.Hash))
                {
                    hash = response.Hash;
                    this.Navigation.NavigateTo($"/contribute/{response.Hash}");
                }
            }
        }
        else
        {
            hash = "The selected folder does not appear to be a Blu-ray structure (missing BDMV folder). Please select the root folder of a Blu-ray.";
        }
    }

    async Task<IEnumerable<FileHashInfo>> HashBluray(FileSystemDirectoryHandleInProcess root)
    {
        List<FileHashInfo> results = new();

        items = await root.ValuesAsync();
        int index = 0;
        foreach (var item in items.Where(i => i.Kind == FileSystemHandleKind.File))
        {
            var file = item as FileSystemFileHandleInProcess;
            if (file != null)
            {
                var fileData = await file.GetFileAsync();
                Console.WriteLine($"File: {file.Name}, Size: {fileData.Size}, Created: {fileData.LastModified}");
                results.Add(new FileHashInfo
                {
                    Index = index++,
                    Name = file.Name,
                    Size = (long)fileData.Size,
                    CreationTime = fileData.LastModified
                });
            }
        }

        return results;
    }
}
