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
    ContributionDiscsQuery Query { get; set; } = null!;

    private IContributionDiscs_MyContributions_Nodes? Contribution { get; set; }
    private IQueryable<IContributionDiscs_MyContributions_Nodes_Discs?> Discs => Contribution!.Discs!.AsQueryable();

    protected override async Task OnInitializedAsync()
    {
        var results = await Query.ExecuteAsync(this.ContributionId);
        if (results != null && results.IsSuccessResult())
        {
            this.Contribution = results.Data!.MyContributions!.Nodes!.FirstOrDefault();
        }
    }

    private async Task DeleteDisc(IContributionDiscs_MyContributions_Nodes_Discs disc)
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
