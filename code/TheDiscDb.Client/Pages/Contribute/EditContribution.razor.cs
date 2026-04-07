using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Components;
using StrawberryShake;
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

    private IContributionDiscs_MyContributions_Nodes? Contribution { get; set; }

    private readonly EditContributionRequest request = new();

    private bool isLoading = true;
    private string? errorMessage;

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

        var response = await ContributionClient.UpdateContribution.ExecuteAsync(new UpdateContributionInput
        {
            ContributionId = ContributionId!,
            Asin = request.Asin,
            Upc = request.Upc,
            ReleaseDate = request.ReleaseDate,
            ReleaseTitle = request.ReleaseTitle,
            ReleaseSlug = request.ReleaseSlug,
            Locale = request.Locale,
            RegionCode = request.RegionCode
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
                _ => "Failed to save changes."
            };
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
