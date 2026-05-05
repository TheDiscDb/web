using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Components;
using StrawberryShake;
using TheDiscDb.Client.Contributions;

namespace TheDiscDb.Client.Pages.Contribute;

[Authorize]
public partial class EngramContribution : CancellableComponentBase
{
    [Parameter]
    public string? ContributionId { get; set; }

    [Inject]
    public IContributionClient ContributionClient { get; set; } = default!;

    [Inject]
    public NavigationManager NavigationManager { get; set; } = default!;

    private IEngramContribution_MyContributions_Nodes? Contribution { get; set; }

    private List<IEngramContribution_MyContributions_Nodes_Discs>? discList;

    private bool IsCompleteButtonDisabled => discList == null || discList.Count == 0;

    private bool IsEditable => Contribution?.Status is
        UserContributionStatus.Pending or
        UserContributionStatus.ChangesRequested or
        UserContributionStatus.Rejected;

    protected override async Task OnInitializedAsync()
    {
        if (this.ContributionClient == null)
        {
            throw new Exception("Contribution Service was not injected");
        }

        var result = await this.ContributionClient.EngramContribution.ExecuteAsync(ContributionId!, this.CancellationToken);
        if (result != null && result.IsSuccessResult())
        {
            this.Contribution = result.Data!.MyContributions!.Nodes!.FirstOrDefault();
            if (this.Contribution != null)
            {
                this.discList = this.Contribution.Discs!
                    .OrderBy(d => d.Index ?? int.MaxValue)
                    .ThenBy(d => d.EncodedId)
                    .ToList();
            }
        }
    }

    private string GetStatusBadgeClass()
    {
        if (Contribution == null)
        {
            return "secondary";
        }

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
