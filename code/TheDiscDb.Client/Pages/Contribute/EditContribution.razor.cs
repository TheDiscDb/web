using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Components;
using StrawberryShake;
using Syncfusion.Blazor.Inputs;
using TheDiscDb.Client.Contributions;

namespace TheDiscDb.Client.Pages.Contribute;

[Authorize]
public partial class EditContribution : ComponentBase
{
    [Parameter]
    public string? ContributionId { get; set; }

    [Inject]
    public IContributionClient ContributionClient { get; set; } = default!;

    [Inject]
    public NavigationManager Navigation { get; set; } = default!;

    [Inject]
    public HttpClient HttpClient { get; set; } = default!;

    private IContributionDiscs_MyContributions_Nodes? Contribution { get; set; }

    private readonly EditContributionRequest request = new();

    private bool isLoading = true;
    private string? errorMessage;
    private string? successMessage;
    private bool imageUpdatePending;
    private bool backImageDeleted;
    private long imageVersion;

    private string? currentFrontImageUrl;
    private string? currentBackImageUrl;

    private SfUploader? frontImageUploader;
    private SfUploader? backImageUploader;

    private string frontImageUploadUrl => $"/api/contribute/{ContributionId}/images/front/upload";
    private string backImageUploadUrl => $"/api/contribute/{ContributionId}/images/back/upload";

    private bool IsEditable => Contribution?.Status is
        UserContributionStatus.Pending or
        UserContributionStatus.ChangesRequested or
        UserContributionStatus.Rejected;

    protected override async Task OnInitializedAsync()
    {
        var result = await ContributionClient.ContributionDiscs.ExecuteAsync(ContributionId!);
        if (result != null && result.IsSuccessResult())
        {
            Contribution = result.Data!.MyContributions!.Nodes!.FirstOrDefault();
            if (Contribution != null)
            {
                request.Asin = Contribution.Asin ?? string.Empty;
                request.Upc = Contribution.Upc ?? string.Empty;
                request.ReleaseDate = Contribution.ReleaseDate;
                request.ReleaseTitle = Contribution.ReleaseTitle ?? string.Empty;
                request.ReleaseSlug = Contribution.ReleaseSlug ?? string.Empty;
                request.Locale = Contribution.Locale ?? string.Empty;
                request.RegionCode = Contribution.RegionCode ?? string.Empty;

                currentFrontImageUrl = Contribution.FrontImageUrl;
                currentBackImageUrl = Contribution.BackImageUrl;
            }
            else
            {
                errorMessage = "Contribution not found.";
            }
        }
        else
        {
            errorMessage = "Failed to load contribution.";
        }

        isLoading = false;
    }

    private async Task HandleValidSubmit()
    {
        errorMessage = null;
        successMessage = null;

        var response = await ContributionClient.UpdateContribution.ExecuteAsync(new UpdateContributionInput
        {
            ContributionId = ContributionId!,
            Asin = request.Asin,
            Upc = request.Upc,
            ReleaseDate = request.ReleaseDate,
            ReleaseTitle = request.ReleaseTitle,
            ReleaseSlug = request.ReleaseSlug,
            Locale = request.Locale,
            RegionCode = request.RegionCode,
            FrontImageUrl = currentFrontImageUrl,
            BackImageUrl = backImageDeleted ? null : currentBackImageUrl,
            DeleteBackImage = backImageDeleted
        });

        if (response.IsSuccessResult() && response.Data?.UpdateContribution?.Errors is not { Count: > 0 })
        {
            Navigation.NavigateTo($"/contribution/{ContributionId}");
        }
        else
        {
            var firstError = response.Data?.UpdateContribution?.Errors?.FirstOrDefault();
            errorMessage = firstError switch
            {
                IUpdateContribution_UpdateContribution_Errors_ContributionNotFoundError e => e.Message,
                IUpdateContribution_UpdateContribution_Errors_AuthenticationError e => e.Message,
                IUpdateContribution_UpdateContribution_Errors_InvalidIdError e => e.Message,
                IUpdateContribution_UpdateContribution_Errors_InvalidOwnershipError e => e.Message,
                IUpdateContribution_UpdateContribution_Errors_InvalidContributionStatusError e => e.Message,
                _ => "Failed to save changes."
            };
        }
    }

    private void FrontImageUploadSuccess(SuccessEventArgs args)
    {
        imageVersion = DateTimeOffset.UtcNow.Ticks;
        imageUpdatePending = true;
        StateHasChanged();
    }

    private void FrontImageUploadFailure(FailureEventArgs args)
    {
        errorMessage = $"Failed to upload front image: {args.Response}";
        StateHasChanged();
    }

    private void BackImageUploadSuccess(SuccessEventArgs args)
    {
        string encodedId = ContributionId!;
        currentBackImageUrl = $"/images/Contributions/{encodedId}/back.jpg";
        backImageDeleted = false;
        imageVersion = DateTimeOffset.UtcNow.Ticks;
        imageUpdatePending = true;
        StateHasChanged();
    }

    private void BackImageUploadFailure(FailureEventArgs args)
    {
        errorMessage = $"Failed to upload back image: {args.Response}";
        StateHasChanged();
    }

    private async Task DeleteBackImage()
    {
        errorMessage = null;
        try
        {
            var response = await HttpClient.PostAsync($"/api/contribute/{ContributionId}/images/back/delete", null);
            if (response.IsSuccessStatusCode)
            {
                currentBackImageUrl = null;
                backImageDeleted = true;
                if (backImageUploader != null)
                {
                    await backImageUploader.ClearAllAsync();
                }
            }
            else
            {
                errorMessage = "Failed to delete back image.";
            }
        }
        catch (Exception ex)
        {
            errorMessage = $"Failed to delete back image: {ex.Message}";
        }
    }
}

public class EditContributionRequest
{
    [Required]
    [RegularExpression(@"\w{10}", ErrorMessage = "ASIN must be a combination of 10 characters or numbers")]
    public string Asin { get; set; } = string.Empty;

    [Required]
    [RegularExpression(@"\d{12,13}", ErrorMessage = "UPC/EAN must be exactly 12 or 13 digits")]
    public string Upc { get; set; } = string.Empty;

    [Required]
    public DateTimeOffset ReleaseDate { get; set; }

    [Required]
    public string ReleaseTitle { get; set; } = string.Empty;

    [Required]
    public string ReleaseSlug { get; set; } = string.Empty;

    public string Locale { get; set; } = string.Empty;

    public string RegionCode { get; set; } = string.Empty;
}
