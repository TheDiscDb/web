using KristofferStrube.Blazor.FileSystem;
using KristofferStrube.Blazor.FileSystemAccess;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;
using TheDiscDb.Core.DiscHash;
using TheDiscDb.Services;

namespace TheDiscDb.Client.Pages.Contribute;

[Authorize]
public partial class AddDisc : ComponentBase
{
    [Parameter]
    public string? ContributionId { get; set; }

    [Inject]
    public IFileSystemAccessServiceInProcess FileSystemAccessService { get; set; } = default!;

    [Inject]
    public IJSRuntime Js { get; set; } = default!;

    [Inject]
    public ApiClient ApiClient { get; set; } = default!;

    [Inject]
    public IUserContributionService Client { get; set; } = default!;

    [Inject]
    public NavigationManager Navigation { get; set; } = default!;

    FileSystemDirectoryHandleInProcess? handler;
    IFileSystemHandleInProcess[] items = Array.Empty<IFileSystemHandleInProcess>();
    string hash = string.Empty;
    private readonly SaveDiscRequest request = new();
    readonly string[] formats = [ "4K", "Blu-ray", "DVD" ];

    protected override Task OnInitializedAsync()
    {
        return Task.CompletedTask;
    }

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
                var response = await this.ApiClient.HashAsync(request);
                if (response != null && !string.IsNullOrEmpty(response.Hash))
                {
                    hash = response.Hash;
                    this.request.ContentHash = hash;
                    //this.Navigation.NavigateTo($"/contribution/{ContributionId}/adddisc/{response.Hash}");
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

    async Task HandleValidSubmit()
    {
        var response = await this.Client.CreateDisc(this.ContributionId!, this.request);
        if (response.IsSuccess)
        {
            this.Navigation!.NavigateTo($"/contribution/{this.ContributionId}/discs/{response.Value.DiscId}");
        }
    }

    private void DiscTitleChanged(ChangeEventArgs args)
    {
        if (args?.Value != null)
        {
            string title = args.Value.ToString()!;

            if (!string.IsNullOrEmpty(title))
            {
                this.request.Slug = title.Slugify();
            }
        }
    }
}
