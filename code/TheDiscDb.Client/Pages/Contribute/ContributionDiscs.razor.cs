using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Components;
using TheDiscDb.Services;
using TheDiscDb.Web.Data;

namespace TheDiscDb.Client.Pages.Contribute;

[Authorize]
public partial class ContributionDiscs : ComponentBase
{
    [Parameter]
    public string? ContributionId { get; set; }

    [Inject]
    IUserContributionService? ContributionService { get; set; }

    [Inject]
    public NavigationManager NavigationManager { get; set; } = default!;

    private UserContribution? Contribution { get; set; }

    private IQueryable<UserContributionDisc>? Discs => Contribution?.Discs.AsQueryable();

    public bool IsCompleteButtonDisabled => Discs == null || !Discs.Any();

    protected override async Task OnInitializedAsync()
    {
        if (this.ContributionService == null)
        {
            throw new Exception("Contribution Service was not injected");
        }

        var result = await this.ContributionService.GetContribution(ContributionId!);
        if (result.IsSuccess)
        {
            this.Contribution = result.Value;
        }
    }

    private string GetStatusBadgeClass()
    {
        if (Contribution == null)
            return "secondary";

        return Contribution.Status switch
        {
            UserContributionStatus.Pending => "info",
            UserContributionStatus.Approved => "success",
            UserContributionStatus.Rejected => "danger",
            _ => "secondary"
        };
    }

    private void NavigateToReview()
        => NavigationManager.NavigateTo($"/contribution/{ContributionId}/review");
}
