using KristofferStrube.Blazor.FileAPI;
using KristofferStrube.Blazor.FileSystem;
using KristofferStrube.Blazor.FileSystemAccess;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;
using Syncfusion.Blazor.Popups;
using TheDiscDb.Core.DiscHash;
using TheDiscDb.Services;
using TheDiscDb.Web.Data;

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
    public IUserContributionService Client { get; set; } = default!;

    [Inject]
    public NavigationManager Navigation { get; set; } = default!;

    [Inject]
    public GetDiscDetailByContentHashQuery? Query { get; set; }

    [Inject]
    public SfDialogService DialogService { get; set; } = default!;

    FileSystemDirectoryHandleInProcess? handler;
    IFileSystemHandleInProcess[] items = Array.Empty<IFileSystemHandleInProcess>();
    string hash = string.Empty;
    List<FileHashInfo>? hashItems = null;
    UserContribution? contribution = null;

    private readonly SaveDiscRequest request = new SaveDiscRequest
    {
        Format = "Blu-ray"
    };

    readonly string[] formats = [ "4K", "Blu-ray", "DVD" ];

    protected override async Task OnInitializedAsync()
    {
        var response = await this.Client.GetContribution(ContributionId!);
        if (response.IsSuccess)
        {
            this.contribution = response.Value;
        }
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
        var videoTs = items.FirstOrDefault(i => i.Kind == FileSystemHandleKind.Directory && i.Name.Equals("VIDEO_TS", StringComparison.OrdinalIgnoreCase));
        
        if (bdmv != default && bdmv is FileSystemDirectoryHandleInProcess bdmvDirectory)
        {
            items = await bdmvDirectory.ValuesAsync();
            var stream = items.FirstOrDefault(i => i.Kind == FileSystemHandleKind.Directory && i.Name.Equals("STREAM", StringComparison.OrdinalIgnoreCase));
            if (stream != default && stream is FileSystemDirectoryHandleInProcess streamDirectory)
            {
                hashItems = await GetHashItems(streamDirectory, i => i.Name.EndsWith("m2ts", StringComparison.OrdinalIgnoreCase));
            }
        }
        else if (videoTs != default && videoTs is FileSystemDirectoryHandleInProcess videoTsDirectory)
        {
            request.Format = "DVD";
            hashItems = await GetHashItems(videoTsDirectory);
        }
        else
        {
            // TODO: Show error to user
            Console.WriteLine("No BDMV or VIDEO_TS folder found.");
        }

        if (hashItems != null)
        {
            var request = new HashDiscRequest
            {
                Files = hashItems
            };

            // TODO: Get a cancellation token
            var response = await this.Client.HashDisc(this.ContributionId!, request);

            if (response != null && response.IsSuccess && response.Value != null)
            {
                hash = response.Value.DiscHash;
                this.request.ContentHash = hash;

                // TODO: Check for this disc in the current user's submissions

                var result = await Query!.ExecuteAsync(hash);
                if (result.Data?.MediaItems?.Nodes != null)
                {
                    if (result.Data?.MediaItems?.Nodes.Count > 0)
                    {
                        bool copyDisc = await DialogService.ConfirmAsync("This disc is already found in another release. Would you like to copy that disc into this contribution?", "Copy Existing Disc");
                        if (copyDisc)
                        {
                            var source = result.Data?.MediaItems?.Nodes.First();
                            if (source != null)
                            {
                                this.request.Slug = source.Slug!;
                                this.request.Name = source.Title!;
                                this.request.Format = source.Type!;

                                var actualDisc = source.Releases.First().Discs.First();

                                this.request.ExistingDiscPath = UserContributionDisc.GenerateDiscPath(this.contribution!.MediaType, source.Externalids.Tmdb, this.contribution.ReleaseSlug, actualDisc!.Slug!);

                                var createDiscResponse = await this.Client.CreateDisc(this.ContributionId!, this.request);
                                if (createDiscResponse.IsSuccess)
                                {
                                    this.Navigation!.NavigateTo($"/contribution/{this.ContributionId}");
                                }
                            }
                        }
                    }
                }
            }
        }
    }

    async Task<List<FileHashInfo>> GetHashItems(FileSystemDirectoryHandleInProcess root, Predicate<FileInProcess>? filter = null)
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
                if (filter != null && !filter(fileData))
                {
                    continue;
                }

                results.Add(new FileHashInfo
                {
                    Index = index++,
                    Name = file.Name,
                    Size = (long)fileData.Size,
                    CreationTime = fileData.LastModified
                });
            }
        }

        return results.ToList();
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
