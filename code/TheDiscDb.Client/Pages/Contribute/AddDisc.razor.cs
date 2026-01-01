using System;
using System.ComponentModel.DataAnnotations;
using KristofferStrube.Blazor.FileAPI;
using KristofferStrube.Blazor.FileSystem;
using KristofferStrube.Blazor.FileSystemAccess;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;
using StrawberryShake;
using Syncfusion.Blazor.Popups;
using TheDiscDb.Client.Contributions;
using TheDiscDb.Core.DiscHash;
using TheDiscDb.Services;
using TheDiscDb.Web.Data;

namespace TheDiscDb.Client.Pages.Contribute;

public class SaveDiscRequest
{
    [Required]
    public string ContentHash { get; set; } = string.Empty;
    [Required]
    public string Format { get; set; } = string.Empty;
    [Required]
    public string Name { get; set; } = string.Empty;
    [Required]
    public string Slug { get; set; } = string.Empty;
    public string? ExistingDiscPath { get; set; }
}

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
    public IContributionClient ContributionClient { get; set; } = default!;

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
    IContributionDiscs_MyContributions_Nodes? contribution = null;

    private readonly SaveDiscRequest request = new SaveDiscRequest
    {
        Format = "Blu-ray"
    };

    readonly string[] formats = [ "4K", "Blu-ray", "DVD" ];

    protected override async Task OnInitializedAsync()
    {
        if (string.IsNullOrEmpty(ContributionId))
        {
            return;
        }

        if (!OperatingSystem.IsBrowser())
        {
            // prerender pass – wait for the interactive render
            return;
        }

        await LoadContributionAsync();
    }

    private async Task LoadContributionAsync()
    {
        var response = await ContributionClient.ContributionDiscs.ExecuteAsync(ContributionId!);
        if (response.IsSuccessResult())
        {
            contribution = response.Data?.MyContributions?.Nodes?.FirstOrDefault();
        }
        else
        {
            // handle/display response.Errors
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
            // TODO: Get a cancellation token
            var hashInput = new HashDiscInput
            {
                ContributionId = this.ContributionId!,
                Files = hashItems.Select(i => new FileHashInfoInput
                {
                    Index = i.Index,
                    Name = i.Name,
                    Size = i.Size,
                    CreationTime = i.CreationTime
                }).ToList()
            };
            var response = await this.ContributionClient.HashDisc.ExecuteAsync(hashInput);

            if (response != null && response.IsSuccessResult() && response.Data != null)
            {
                hash = response.Data!.HashDisc!.DiscHash!.Hash;
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

                                this.request.ExistingDiscPath = UserContributionDisc.GenerateDiscPath(this.contribution!.MediaType!, source.Externalids.Tmdb!, this.contribution.ReleaseSlug!, actualDisc!.Slug!);

                                var createInput = new CreateDiscInput
                                {
                                    ContributionId = this.ContributionId!,
                                    Name = this.request.Name!,
                                    Slug = this.request.Slug!,
                                    Format = this.request.Format!,
                                    ExistingDiscPath = this.request.ExistingDiscPath
                                };
                                var createDiscResponse = await this.ContributionClient.CreateDisc.ExecuteAsync(createInput);
                                if (createDiscResponse.IsSuccessResult())
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
        var input = new CreateDiscInput
        {
            ContributionId = this.ContributionId!,
            Name = this.request.Name!,
            Slug = this.request.Slug!,
            Format = this.request.Format!,
            ContentHash = this.request.ContentHash
        };
        var response = await this.ContributionClient.CreateDisc.ExecuteAsync(input);
        if (response.IsSuccessResult())
        {
            this.Navigation!.NavigateTo($"/contribution/{this.ContributionId}/discs/{response.Data!.CreateDisc.UserContributionDisc!.EncodedId}");
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
