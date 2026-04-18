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

    private List<IContributionDiscs_MyContributions_Nodes_Discs>? discList;
    private IQueryable<IContributionDiscs_MyContributions_Nodes_Discs?>? Discs => discList?.AsQueryable();

    public bool IsCompleteButtonDisabled => discList == null || discList.Count == 0;

    private bool IsEditable => Contribution?.Status is
        UserContributionStatus.Pending or
        UserContributionStatus.ChangesRequested or
        UserContributionStatus.Rejected;

    private bool isSaving;

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

    private async Task MoveDiscUp(IContributionDiscs_MyContributions_Nodes_Discs disc)
    {
        if (discList == null || isSaving) return;
        int idx = discList.IndexOf(disc);
        if (idx <= 0) return;

        (discList[idx - 1], discList[idx]) = (discList[idx], discList[idx - 1]);
        await SaveDiscOrder();
    }

    private async Task MoveDiscDown(IContributionDiscs_MyContributions_Nodes_Discs disc)
    {
        if (discList == null || isSaving) return;
        int idx = discList.IndexOf(disc);
        if (idx < 0 || idx >= discList.Count - 1) return;

        (discList[idx], discList[idx + 1]) = (discList[idx + 1], discList[idx]);
        await SaveDiscOrder();
    }

    private async Task SaveDiscOrder()
    {
        if (discList == null) return;

        isSaving = true;
        try
        {
            var input = new ReorderDiscsInput
            {
                ContributionId = ContributionId!,
                DiscIds = discList.Select(d => d.EncodedId).ToList()
            };

            await this.ContributionClient.ReorderDiscs.ExecuteAsync(input);
        }
        finally
        {
            isSaving = false;
        }
    }
}
