using System;
using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using StrawberryShake;
using Syncfusion.Blazor.Popups;
using TheDiscDb.Client.Contributions;
using TheDiscDb.Client.Controls;
using TheDiscDb.Client.Interop;
using TheDiscDb.Core.DiscHash;
using TheDiscDb.InputModels;
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
    public string? Fingerprint { get; set; }
}

[Authorize]
public partial class AddDisc : CancellableComponentBase
{
    [Parameter]
    public string? ContributionId { get; set; }

    [Inject]
    public DiscDirectoryPicker DiscDirectoryPicker { get; set; } = default!;

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

    string hash = string.Empty;
    IContributionDiscs_MyContributions_Nodes? contribution = null;
    bool discSelected;
    bool isScanning;
    bool manualHashMode;
    string manualHash = string.Empty;
    SlugInput? slugInput;
    string? copyFlowError;
    IGetIntakeDiscMatch_IntakeDiscMatch? intakeDiscMatch;
    private ContributionNamingSuggestion? DiscNamingSuggestion =>
        ContributionInputGuard.GetNamingSuggestion(
            this.contribution?.Title,
            this.request.Name,
            this.request.Slug,
            title => title.Slugify());

    bool IsDevelopmentMode=> HostEnvironment.Environment == "Development";

    private readonly SaveDiscRequest request = new SaveDiscRequest
    {
        Format = DiscFormatConstants.BluRay
    };

    readonly string[] formats = [.. DiscFormatConstants.ContributionFormats];

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
        this.copyFlowError = null;
        try
        {
            await using var selection = await this.DiscDirectoryPicker.PickAsync(this.CancellationToken);
            if (selection is null)
            {
                return;
            }

            this.isScanning = true;
            await TryCalculateHash(selection.Files);
        }
        catch (OperationCanceledException) when (this.CancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            Console.WriteLine(ex);
            this.copyFlowError = $"Could not read the selected disc folder: {ex.Message}";
        }
        finally
        {
            this.isScanning = false;
        }
    }

    async Task TryCalculateHash(IReadOnlyList<DiscScanFile> files)
    {
        this.copyFlowError = null;
        this.request.GlobalDiscId = null;
        this.request.Fingerprint = null;
        this.request.ExistingDiscPath = null;
        this.intakeDiscMatch = null;
        var scan = await DiscScanner.ScanAsync(files, this.CancellationToken);
        if (!string.IsNullOrEmpty(scan.Error))
        {
            this.copyFlowError = scan.Error;
            return;
        }

        if (scan.HashFiles.Count == 0)
        {
            this.copyFlowError = "No hashable files were found on this disc.";
            return;
        }

        this.request.Format = scan.Format ?? DiscFormatConstants.BluRay;
        this.request.GlobalDiscId = scan.GlobalDiscId;
        var hashInput = new HashDiscInput
        {
            ContributionId = this.ContributionId!,
            Files = scan.HashFiles.Select(item => new FileHashInfoInput
            {
                Index = item.Index,
                Name = item.Name,
                Size = item.Size,
                CreationTime = item.CreationTime
            }).ToList(),
            FingerprintFiles = scan.FingerprintFiles.Select(item => new DiscFingerprintFileInput
            {
                Path = item.Path,
                Size = item.Size,
            }).ToList(),
        };
        var response = await this.ContributionClient.HashDisc.ExecuteAsync(
            hashInput,
            this.CancellationToken);

        if (!response.IsSuccessResult() || response.Data?.HashDisc?.DiscHash is null)
        {
            this.copyFlowError = response.Errors?.FirstOrDefault()?.Message
                ?? "Could not identify this disc. Please try again.";
            return;
        }

        hash = response.Data.HashDisc.DiscHash.Hash;
        this.request.Fingerprint = response.Data.HashDisc.DiscHash.Fingerprint;
        this.request.ContentHash = hash;
        this.discSelected = true;

        var existingDiscResult = await TryCopyFromExistingDisc(hash);
        if (existingDiscResult.Copied)
        {
            return;
        }

        if (!existingDiscResult.Found)
        {
            await TryApplyIntakeDiscMatchAsync(hash);
        }
    }

    async Task HandleValidSubmit()
    {
        this.copyFlowError = null;
        if (this.intakeDiscMatch is not null)
        {
            await PromoteIntakeDiscAsync();
            return;
        }

        var input = new CreateDiscInput
        {
            ContributionId = this.ContributionId!,
            Name = this.request.Name!,
            Slug = this.request.Slug!,
            Format = this.request.Format!,
            ContentHash = this.request.ContentHash,
            ExistingDiscPath = this.request.ExistingDiscPath,
            GlobalDiscId = this.request.GlobalDiscId,
            Fingerprint = this.request.Fingerprint,
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
                ICreateDisc_CreateDisc_Errors_InvalidDiscPathError e => e.Message,
                _ => $"Could not save disc ({payloadError.Code})."
            };
        }

        return response.Errors?.FirstOrDefault()?.Message;
    }

    private static string NormalizeFormat(string? format)
    {
        if (string.IsNullOrWhiteSpace(format))
        {
            return DiscFormatConstants.BluRay;
        }

        var normalized = format.Trim();
        if (normalized.Contains(DiscFormatConstants.FourK, StringComparison.OrdinalIgnoreCase) ||
            normalized.Contains(DiscFormatConstants.Uhd, StringComparison.OrdinalIgnoreCase))
        {
            return DiscFormatConstants.FourK;
        }

        if (normalized.Contains(DiscFormatConstants.Dvd, StringComparison.OrdinalIgnoreCase))
        {
            return DiscFormatConstants.Dvd;
        }

        if (normalized.Contains(DiscFormatConstants.BluRay, StringComparison.OrdinalIgnoreCase))
        {
            return DiscFormatConstants.BluRay;
        }

        return normalized;
    }

    private async Task DiscTitleChanged(ChangeEventArgs args)
    {
        if (args?.Value != null)
        {
            string title = args.Value.ToString()!;
            this.request.Name = title;

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

    private async Task ApplyDiscNamingSuggestion()
    {
        var suggestion = this.DiscNamingSuggestion;
        if (suggestion?.CanApply != true)
        {
            return;
        }

        this.request.Name = suggestion.SuggestedName!;
        this.request.Slug = suggestion.SuggestedSlug!;
        if (this.slugInput != null)
        {
            await this.slugInput.RecheckAvailability(this.request.Slug);
        }
    }

    private async Task SubmitManualHash()
    {
        if (!string.IsNullOrWhiteSpace(manualHash))
        {
            this.copyFlowError = null;
            this.intakeDiscMatch = null;
            request.ContentHash = manualHash.Trim();
            request.ExistingDiscPath = null;
            request.Fingerprint = null;
            manualHashMode = true;

            var existingDiscResult = await TryCopyFromExistingDisc(request.ContentHash);
            if (!existingDiscResult.Found)
            {
                await TryApplyIntakeDiscMatchAsync(request.ContentHash);
            }
        }
    }

    private async Task<ExistingDiscCopyResult> TryCopyFromExistingDisc(string discHash)
    {
        if (string.IsNullOrWhiteSpace(discHash))
        {
            return new ExistingDiscCopyResult(false, false);
        }

        var result = await Query!.ExecuteAsync(discHash, templates: null, cancellationToken: this.CancellationToken);
        if (result.Data?.MediaItems?.Nodes == null || result.Data.MediaItems.Nodes.Count == 0)
        {
            return new ExistingDiscCopyResult(false, false);
        }

        bool copyDisc = await DialogService.ConfirmAsync("This disc is already found in another release. Would you like to copy that disc into this contribution?", "Copy Existing Disc");
        if (!copyDisc)
        {
            return new ExistingDiscCopyResult(true, false);
        }

        var source = result.Data.MediaItems.Nodes.First();
        if (source == null)
        {
            return new ExistingDiscCopyResult(true, false);
        }

        var sourceRelease = source.Releases
            .FirstOrDefault(release => release.Discs.Any(disc => disc.ContentHash == discHash));
        var sourceDisc = sourceRelease?.Discs.FirstOrDefault(disc => disc.ContentHash == discHash);
        if (sourceRelease == null || sourceDisc == null)
        {
            return new ExistingDiscCopyResult(true, false);
        }

        var discKey = !string.IsNullOrWhiteSpace(sourceDisc.Slug)
            ? sourceDisc.Slug
            : sourceDisc.Index.ToString();

        this.request.Slug = discKey;
        this.request.Name = sourceDisc.Name!;
        this.request.Format = NormalizeFormat(sourceDisc.Format);
        this.request.ContentHash = discHash;

        this.request.ExistingDiscPath = UserContributionDisc.GenerateDiscPath(
            source.Type!,
            source.Externalids.Tmdb!,
            sourceRelease.Slug!,
            discKey);

        var createInput = new CreateDiscInput
        {
            ContributionId = this.ContributionId!,
            Name = this.request.Name!,
            Slug = this.request.Slug!,
            Format = this.request.Format!,
            ContentHash = this.request.ContentHash,
            ExistingDiscPath = this.request.ExistingDiscPath,
            GlobalDiscId = this.request.GlobalDiscId,
            Fingerprint = this.request.Fingerprint,
        };
        var createDiscResponse = await this.ContributionClient.CreateDisc.ExecuteAsync(createInput, this.CancellationToken);
        if (!createDiscResponse.IsSuccessResult())
        {
            this.copyFlowError = GetCreateDiscErrorMessage(createDiscResponse)
                ?? "Could not auto-copy this disc. You can still save it manually below.";
            return new ExistingDiscCopyResult(true, false);
        }

        if (createDiscResponse.Data?.CreateDisc?.Errors is { Count: > 0 })
        {
            this.copyFlowError = GetCreateDiscErrorMessage(createDiscResponse)
                ?? "Could not auto-copy this disc. You can still save it manually below.";
            return new ExistingDiscCopyResult(true, false);
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

        return new ExistingDiscCopyResult(true, true);
    }

    private async Task TryApplyIntakeDiscMatchAsync(string discHash)
    {
        var response = await this.ContributionClient.GetIntakeDiscMatch.ExecuteAsync(
            this.ContributionId!,
            discHash,
            this.request.Format,
            this.request.GlobalDiscId,
            this.CancellationToken);
        if (!response.IsSuccessResult() || response.Data?.IntakeDiscMatch is not { } match)
        {
            return;
        }

        this.intakeDiscMatch = match;
        if (!string.IsNullOrWhiteSpace(match.Format))
        {
            this.request.Format = NormalizeFormat(match.Format);
        }

        if (string.IsNullOrWhiteSpace(this.request.Name) && !string.IsNullOrWhiteSpace(match.Name))
        {
            this.request.Name = match.Name;
        }

        if (string.IsNullOrWhiteSpace(this.request.Slug) && !string.IsNullOrWhiteSpace(match.Slug))
        {
            this.request.Slug = match.Slug;
            if (this.slugInput != null)
            {
                await this.slugInput.RecheckAvailability(this.request.Slug);
            }
        }
    }

    private async Task PromoteIntakeDiscAsync()
    {
        var response = await this.ContributionClient.PromoteIntakeDisc.ExecuteAsync(
            new PromoteIntakeDiscInput
            {
                ContributionId = this.ContributionId!,
                ContentHash = this.request.ContentHash,
                Format = this.request.Format,
                Name = this.request.Name,
                Slug = this.request.Slug,
                GlobalDiscId = this.request.GlobalDiscId,
            },
            this.CancellationToken);

        var payload = response.Data?.PromoteIntakeDisc;
        if (!response.IsSuccessResult() || payload?.Errors is { Count: > 0 })
        {
            this.copyFlowError = GetPromoteIntakeDiscErrorMessage(payload?.Errors?.FirstOrDefault())
                ?? response.Errors?.FirstOrDefault()?.Message
                ?? "Could not promote the matching intake disc.";
            return;
        }

        var result = payload?.IntakeDiscPromotionResult;
        if (result?.MainDatabaseMatch == true)
        {
            var existingDiscResult = await TryCopyFromExistingDisc(this.request.ContentHash);
            if (!existingDiscResult.Copied)
            {
                this.copyFlowError = "This disc is now available in the database. Use the existing-disc copy option to continue.";
            }
            return;
        }

        var createdDiscId = result?.Disc.EncodedId;
        if (string.IsNullOrWhiteSpace(createdDiscId))
        {
            this.copyFlowError = "The intake disc could not be added to this contribution.";
            return;
        }

        var target = result!.LogsCopied
            ? $"/contribution/{this.ContributionId}/discs/{createdDiscId}/identify"
            : $"/contribution/{this.ContributionId}/discs/{createdDiscId}";
        this.Navigation.NavigateTo(target);
    }

    private static string? GetPromoteIntakeDiscErrorMessage(
        IPromoteIntakeDisc_PromoteIntakeDisc_Errors? error) =>
        error switch
        {
            IPromoteIntakeDisc_PromoteIntakeDisc_Errors_ContributionNotFoundError e => e.Message,
            IPromoteIntakeDisc_PromoteIntakeDisc_Errors_AuthenticationError e => e.Message,
            IPromoteIntakeDisc_PromoteIntakeDisc_Errors_InvalidIdError e => e.Message,
            IPromoteIntakeDisc_PromoteIntakeDisc_Errors_InvalidOwnershipError e => e.Message,
            IPromoteIntakeDisc_PromoteIntakeDisc_Errors_InvalidContributionStatusError e => e.Message,
            IPromoteIntakeDisc_PromoteIntakeDisc_Errors_IntakeDiscMatchNotFoundError e => e.Message,
            _ => null,
        };

    private sealed record ExistingDiscCopyResult(bool Found, bool Copied);

    private Task<bool> CheckDiscSlugAvailability(string slug, CancellationToken cancellationToken)
    {
        if (this.contribution == null)
        {
            return Task.FromResult(true);
        }

        bool taken = this.contribution.Discs.Any(d =>
            !string.IsNullOrEmpty(d.Slug) &&
            d.Slug.Equals(slug, StringComparison.OrdinalIgnoreCase));

        return Task.FromResult(!taken);
    }
}
