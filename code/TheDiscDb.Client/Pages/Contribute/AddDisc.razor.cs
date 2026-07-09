using System;
using System.ComponentModel.DataAnnotations;
using System.Text.RegularExpressions;
using KristofferStrube.Blazor.FileAPI;
using KristofferStrube.Blazor.FileSystem;
using KristofferStrube.Blazor.FileSystemAccess;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using Microsoft.JSInterop;
using StrawberryShake;
using Syncfusion.Blazor.Popups;
using TheDiscDb.Client.Contributions;
using TheDiscDb.Client.Controls;
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
    public string? GlobalDiscId { get; set; }
}

[Authorize]
public partial class AddDisc : CancellableComponentBase
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

    [Inject]
    public IWebAssemblyHostEnvironment HostEnvironment { get; set; } = default!;

    FileSystemDirectoryHandleInProcess? handler;
    IFileSystemHandleInProcess[] items = Array.Empty<IFileSystemHandleInProcess>();
    string hash = string.Empty;
    List<FileHashInfo>? hashItems = null;
    IContributionDiscs_MyContributions_Nodes? contribution = null;
    bool manualHashMode;
    string manualHash = string.Empty;
    SlugInput? slugInput;
    string? copyFlowError;

    bool IsDevelopmentMode=> HostEnvironment.Environment == "Development";

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
        var response = await ContributionClient.ContributionDiscs.ExecuteAsync(ContributionId!, this.CancellationToken);
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
        this.copyFlowError = null;
        this.request.GlobalDiscId = null;
        var rootItems = await handler!.ValuesAsync();

        var bdmv = rootItems.FirstOrDefault(i => i.Kind == FileSystemHandleKind.Directory && i.Name.Equals("BDMV", StringComparison.OrdinalIgnoreCase));
        var videoTs = rootItems.FirstOrDefault(i => i.Kind == FileSystemHandleKind.Directory && i.Name.Equals("VIDEO_TS", StringComparison.OrdinalIgnoreCase));

        if (bdmv != default && bdmv is FileSystemDirectoryHandleInProcess bdmvDirectory)
        {
            // Blu-ray / UHD: compute the AACS Disc ID from the sibling AACS folder.
            // Optional — absent on MKV-only rips, in which case this stays null.
            this.request.GlobalDiscId = await TryComputeAacsDiscId(rootItems);

            var bdmvItems = await bdmvDirectory.ValuesAsync();
            var stream = bdmvItems.FirstOrDefault(i => i.Kind == FileSystemHandleKind.Directory && i.Name.Equals("STREAM", StringComparison.OrdinalIgnoreCase));
            if (stream != default && stream is FileSystemDirectoryHandleInProcess streamDirectory)
            {
                hashItems = await GetHashItems(streamDirectory, i => i.Name.EndsWith("m2ts", StringComparison.OrdinalIgnoreCase));
            }
        }
        else if (videoTs != default && videoTs is FileSystemDirectoryHandleInProcess videoTsDirectory)
        {
            request.Format = "DVD";
            // DVD: compute the libdvdread DVDDiscID from the IFO files.
            this.request.GlobalDiscId = await TryComputeDvdDiscId(videoTsDirectory);
            hashItems = await GetHashItems(videoTsDirectory);
        }
        else
        {
            // TODO: Show error to user
            Console.WriteLine("No BDMV or VIDEO_TS folder found.");
        }

        if (hashItems != null)
        {
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
            var response = await this.ContributionClient.HashDisc.ExecuteAsync(hashInput, this.CancellationToken);

            if (response != null && response.IsSuccessResult() && response.Data != null)
            {
                hash = response.Data!.HashDisc!.DiscHash!.Hash;
                this.request.ContentHash = hash;

                var copiedExistingDisc = await TryCopyFromExistingDisc(hash);
                if (copiedExistingDisc)
                {
                    return;
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

    private static readonly Regex VtsIfoPattern =
        new(@"^VTS_(\d{2})_0\.IFO$", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    // Computes the AACS Disc ID (SHA-1 of AACS/Unit_Key_RO.inf) for Blu-ray/UHD discs.
    // Best-effort: returns null (and never throws) when the AACS folder or file is absent.
    async Task<string?> TryComputeAacsDiscId(IFileSystemHandleInProcess[] rootItems)
    {
        try
        {
            var aacs = rootItems.FirstOrDefault(i => i.Kind == FileSystemHandleKind.Directory && i.Name.Equals("AACS", StringComparison.OrdinalIgnoreCase)) as FileSystemDirectoryHandleInProcess;
            if (aacs is null)
            {
                return null;
            }

            var aacsItems = await aacs.ValuesAsync();
            var unitKey = FindFileHandle(aacsItems, "Unit_Key_RO.inf");

            // libaacs falls back to AACS/DUPLICATE/Unit_Key_RO.inf.
            if (unitKey is null)
            {
                var duplicate = aacsItems.FirstOrDefault(i => i.Kind == FileSystemHandleKind.Directory && i.Name.Equals("DUPLICATE", StringComparison.OrdinalIgnoreCase)) as FileSystemDirectoryHandleInProcess;
                if (duplicate is not null)
                {
                    unitKey = FindFileHandle(await duplicate.ValuesAsync(), "Unit_Key_RO.inf");
                }
            }

            if (unitKey is null)
            {
                return null;
            }

            return AacsDiscId.Compute(await ReadHandleBytes(unitKey));
        }
        catch (Exception ex)
        {
            Console.WriteLine($"AACS Disc ID computation skipped: {ex.Message}");
            return null;
        }
    }

    // Computes the DVD Disc ID (libdvdread DVDDiscID = MD5 of the IFO files).
    // Best-effort: returns null (and never throws) when VIDEO_TS.IFO is absent.
    async Task<string?> TryComputeDvdDiscId(FileSystemDirectoryHandleInProcess videoTsDirectory)
    {
        try
        {
            var ifoItems = await videoTsDirectory.ValuesAsync();
            var videoTsIfoHandle = FindFileHandle(ifoItems, "VIDEO_TS.IFO");
            if (videoTsIfoHandle is null)
            {
                return null;
            }

            var videoTsIfo = await ReadHandleBytes(videoTsIfoHandle);

            var vtsByNumber = new SortedDictionary<int, byte[]>();
            foreach (var item in ifoItems)
            {
                if (item.Kind != FileSystemHandleKind.File)
                {
                    continue;
                }

                var match = VtsIfoPattern.Match(item.Name);
                if (match.Success && item is FileSystemFileHandleInProcess fileHandle)
                {
                    vtsByNumber[int.Parse(match.Groups[1].Value)] = await ReadHandleBytes(fileHandle);
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

    private static FileSystemFileHandleInProcess? FindFileHandle(IEnumerable<IFileSystemHandleInProcess> items, string name)
        => items.FirstOrDefault(i => i.Kind == FileSystemHandleKind.File && string.Equals(i.Name, name, StringComparison.OrdinalIgnoreCase)) as FileSystemFileHandleInProcess;

    private static async Task<byte[]> ReadHandleBytes(FileSystemFileHandleInProcess handle)
    {
        var file = await handle.GetFileAsync();
        return await file.ArrayBufferAsync();
    }

    async Task HandleValidSubmit()
    {
        this.copyFlowError = null;
        var input = new CreateDiscInput
        {
            ContributionId = this.ContributionId!,
            Name = this.request.Name!,
            Slug = this.request.Slug!,
            Format = this.request.Format!,
            ContentHash = this.request.ContentHash,
            ExistingDiscPath = this.request.ExistingDiscPath,
            GlobalDiscId = this.request.GlobalDiscId
        };
        var response = await this.ContributionClient.CreateDisc.ExecuteAsync(input, this.CancellationToken);
        if (!response.IsSuccessResult())
        {
            this.copyFlowError = GetCreateDiscErrorMessage(response) ?? "Could not save this disc. Please verify the copied disc source and try again.";
            return;
        }

        if (response.Data?.CreateDisc?.Errors is { Count: > 0 })
        {
            this.copyFlowError = GetCreateDiscErrorMessage(response) ?? "Could not save this disc. Please verify the copied disc source and try again.";
            return;
        }

        if (response.IsSuccessResult())
        {
            var createdDiscId = response.Data!.CreateDisc.UserContributionDisc!.EncodedId;

            if (!string.IsNullOrEmpty(this.request.ExistingDiscPath))
            {
                this.Navigation!.NavigateTo($"/contribution/{this.ContributionId}/disc/{createdDiscId}/edit?returnUrl=/contribution/{this.ContributionId}");
                return;
            }

            this.Navigation!.NavigateTo($"/contribution/{this.ContributionId}/discs/{createdDiscId}");
        }
    }

    private static string? GetCreateDiscErrorMessage(IOperationResult<ICreateDiscResult> response)
    {
        if (response.Data?.CreateDisc?.Errors is { Count: > 0 } payloadErrors)
        {
            var payloadError = payloadErrors[0];
            return payloadError switch
            {
                ICreateDisc_CreateDisc_Errors_ContributionNotFoundError e => e.Message,
                ICreateDisc_CreateDisc_Errors_AuthenticationError e => e.Message,
                ICreateDisc_CreateDisc_Errors_InvalidIdError e => e.Message,
                ICreateDisc_CreateDisc_Errors_InvalidOwnershipError e => e.Message,
                _ => $"Could not save disc ({payloadError.Code})."
            };
        }

        return response.Errors?.FirstOrDefault()?.Message;
    }

    private static string NormalizeFormat(string? format)
    {
        if (string.IsNullOrWhiteSpace(format))
        {
            return "Blu-ray";
        }

        if (format.Equals("UHD", StringComparison.OrdinalIgnoreCase))
        {
            return "4K";
        }

        return format;
    }

    private async Task DiscTitleChanged(ChangeEventArgs args)
    {
        if (args?.Value != null)
        {
            string title = args.Value.ToString()!;

            if (!string.IsNullOrEmpty(title))
            {
                this.request.Slug = title.Slugify();
                if (this.slugInput != null)
                {
                    await this.slugInput.RecheckAvailability(this.request.Slug);
                }
            }
        }
    }

    private async Task SubmitManualHash()
    {
        if (!string.IsNullOrWhiteSpace(manualHash))
        {
            this.copyFlowError = null;
            request.ContentHash = manualHash.Trim();
            request.ExistingDiscPath = null;
            manualHashMode = true;

            await TryCopyFromExistingDisc(request.ContentHash);
        }
    }

    private async Task<bool> TryCopyFromExistingDisc(string discHash)
    {
        if (string.IsNullOrWhiteSpace(discHash))
        {
            return false;
        }

        var result = await Query!.ExecuteAsync(discHash, templates: null, cancellationToken: this.CancellationToken);
        if (result.Data?.MediaItems?.Nodes == null || result.Data.MediaItems.Nodes.Count == 0)
        {
            return false;
        }

        bool copyDisc = await DialogService.ConfirmAsync("This disc is already found in another release. Would you like to copy that disc into this contribution?", "Copy Existing Disc");
        if (!copyDisc)
        {
            return false;
        }

        var source = result.Data.MediaItems.Nodes.First();
        if (source == null)
        {
            return false;
        }

        var sourceRelease = source.Releases
            .FirstOrDefault(release => release.Discs.Any(disc => disc.ContentHash == discHash));
        var sourceDisc = sourceRelease?.Discs.FirstOrDefault(disc => disc.ContentHash == discHash);
        if (sourceRelease == null || sourceDisc == null)
        {
            return false;
        }

        this.request.Slug = sourceDisc.Slug!;
        this.request.Name = sourceDisc.Name!;
        this.request.Format = NormalizeFormat(sourceDisc.Format);
        this.request.ContentHash = discHash;
        this.request.ExistingDiscPath = UserContributionDisc.GenerateDiscPath(
            source.Type!,
            source.Externalids.Tmdb!,
            sourceRelease.Slug!,
            sourceDisc.Slug!);

        var createInput = new CreateDiscInput
        {
            ContributionId = this.ContributionId!,
            Name = this.request.Name!,
            Slug = this.request.Slug!,
            Format = this.request.Format!,
            ContentHash = this.request.ContentHash,
            ExistingDiscPath = this.request.ExistingDiscPath,
            GlobalDiscId = this.request.GlobalDiscId
        };
        var createDiscResponse = await this.ContributionClient.CreateDisc.ExecuteAsync(createInput, this.CancellationToken);
        if (!createDiscResponse.IsSuccessResult())
        {
            this.copyFlowError = GetCreateDiscErrorMessage(createDiscResponse)
                ?? "Could not auto-copy this disc. You can still save it manually below.";
            return false;
        }

        if (createDiscResponse.Data?.CreateDisc?.Errors is { Count: > 0 })
        {
            this.copyFlowError = GetCreateDiscErrorMessage(createDiscResponse)
                ?? "Could not auto-copy this disc. You can still save it manually below.";
            return false;
        }

        var createdDiscId = createDiscResponse.Data?.CreateDisc?.UserContributionDisc?.EncodedId;
        if (!string.IsNullOrEmpty(createdDiscId))
        {
            this.Navigation!.NavigateTo($"/contribution/{this.ContributionId}/disc/{createdDiscId}/edit?returnUrl=/contribution/{this.ContributionId}");
        }
        else
        {
            this.Navigation!.NavigateTo($"/contribution/{this.ContributionId}");
        }

        return true;
    }

    private Task<bool> CheckDiscSlugAvailability(string slug, CancellationToken cancellationToken)
    {
        if (this.contribution == null)
        {
            return Task.FromResult(true);
        }

        // Disc slugs only need to be unique within their own release. A contribution
        // always represents a single (new) release, so we only check against the discs
        // already in this contribution. The same disc slug (e.g. "blu-ray") is allowed
        // to exist on other releases of the same media item.
        bool taken = this.contribution.Discs.Any(d =>
            !string.IsNullOrEmpty(d.Slug) &&
            d.Slug.Equals(slug, StringComparison.OrdinalIgnoreCase));

        return Task.FromResult(!taken);
    }
}
