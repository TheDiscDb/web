using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Components;
using StrawberryShake;
using TheDiscDb.Client.Contributions;

namespace TheDiscDb.Client.Pages.Contribute;

[Authorize]
public partial class ContributionDiscs : ComponentBase
{
    [Parameter]
    public string? ContributionId { get; set; }

    [Inject]
    public IContributionClient ContributionClient { get; set; } = default!;

    [Inject]
    public NavigationManager NavigationManager { get; set; } = default!;

    private IContributionDiscs_MyContributions_Nodes? Contribution { get; set; }

    private IQueryable<IContributionDiscs_MyContributions_Nodes_Discs?>? Discs { get; set; }

    public bool IsCompleteButtonDisabled => Discs == null || !Discs.Any();

    protected override async Task OnInitializedAsync()
    {
        if (this.ContributionClient == null)
        {
            throw new Exception("Contribution Service was not injected");
        }

        var result = await this.ContributionClient.ContributionDiscs.ExecuteAsync(ContributionId!);
        if (result != null && result.IsSuccessResult())
        {
            this.Contribution = result.Data!.MyContributions!.Nodes!.FirstOrDefault();
            if (this.Contribution != null)
            {
                this.Discs = this.Contribution.Discs!.AsQueryable();
            }
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
