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

    private UserContribution? Contribution { get; set; }

    private IQueryable<UserContributionDisc>? Discs => Contribution?.Discs.AsQueryable();

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

    private async Task DeleteDisc(UserContributionDisc disc)
    {
        await Task.Delay(1);
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
}
