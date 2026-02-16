using System;
using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
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

public class DirectoryFileInfo
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("size")]
    public long Size { get; set; }

    [JsonPropertyName("lastModified")]
    public double LastModified { get; set; }

    [JsonPropertyName("webkitRelativePath")]
    public string WebkitRelativePath { get; set; } = string.Empty;
}

[Authorize]
public partial class AddDisc : ComponentBase
{
    [Parameter]
    public string? ContributionId { get; set; }

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

    const string FolderInputId = "folder-input";
    bool folderSelected;
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
            var useFileSystemAccess = await Js.InvokeAsync<bool>("directoryPicker.isSupported");
            if (useFileSystemAccess)
            {
                // File System Access API: shows a clean "Select Folder" dialog (no upload language)
                var files = await Js.InvokeAsync<DirectoryFileInfo[]>("directoryPicker.openDirectory");
                if (files is not null && files.Length > 0)
                {
                    await TryCalculateHash(files);
                }
            }
            else
            {
                // Fallback for Firefox and other browsers: triggers the hidden webkitdirectory input
                await Js.InvokeVoidAsync("directoryPicker.clickElement", FolderInputId);
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine(ex);
        }
    }

    async Task OnFolderSelected()
    {
        try
        {
            var files = await Js.InvokeAsync<DirectoryFileInfo[]>("directoryPicker.getDirectoryFiles", FolderInputId);
            if (files is null || files.Length == 0) return;

            await TryCalculateHash(files);
        }
        catch (Exception ex)
        {
            Console.WriteLine(ex);
        }
    }

    async Task TryCalculateHash(DirectoryFileInfo[] files)
    {
        // The webkitRelativePath is like "RootFolder/BDMV/STREAM/00001.m2ts"
        // Split into path segments and look for BDMV/STREAM or VIDEO_TS directories
        var bdmvStreamFiles = files.Where(f =>
        {
            var segments = f.WebkitRelativePath.Split('/');
            // Look for pattern: .../BDMV/STREAM/file.m2ts
            return segments.Length >= 3
                && segments[^3].Equals("BDMV", StringComparison.OrdinalIgnoreCase)
                && segments[^2].Equals("STREAM", StringComparison.OrdinalIgnoreCase)
                && f.Name.EndsWith(".m2ts", StringComparison.OrdinalIgnoreCase);
        }).ToArray();

        var videoTsFiles = files.Where(f =>
        {
            var segments = f.WebkitRelativePath.Split('/');
            // Look for pattern: .../VIDEO_TS/file
            return segments.Length >= 2
                && segments[^2].Equals("VIDEO_TS", StringComparison.OrdinalIgnoreCase);
        }).ToArray();

        if (bdmvStreamFiles.Length > 0)
        {
            hashItems = GetHashItems(bdmvStreamFiles);
        }
        else if (videoTsFiles.Length > 0)
        {
            request.Format = "DVD";
            hashItems = GetHashItems(videoTsFiles);
        }
        else
        {
            // TODO: Show error to user
            Console.WriteLine("No BDMV or VIDEO_TS folder found.");
        }

        if (hashItems != null)
        {
            folderSelected = true;

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

    List<FileHashInfo> GetHashItems(DirectoryFileInfo[] files)
    {
        List<FileHashInfo> results = new();

        int index = 0;
        foreach (var file in files)
        {
            results.Add(new FileHashInfo
            {
                Index = index++,
                Name = file.Name,
                Size = file.Size,
                CreationTime = DateTimeOffset.FromUnixTimeMilliseconds((long)file.LastModified).UtcDateTime
            });
        }

        return results;
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
