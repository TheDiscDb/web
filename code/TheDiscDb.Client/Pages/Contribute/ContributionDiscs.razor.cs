using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Components;
using StrawberryShake;
using TheDiscDb.Client.Contributions;

namespace TheDiscDb.Client.Pages.Contribute;

[Authorize]
public partial class ContributionDiscs : CancellableComponentBase
{
    [Parameter]
    public string? ContributionId { get; set; }

    [Inject]
    public IContributionClient ContributionClient { get; set; } = default!;

    [Inject]
    public NavigationManager NavigationManager { get; set; } = default!;

    private IContributionDiscs_MyContributions_Nodes? Contribution { get; set; }

    private List<IContributionDiscs_MyContributions_Nodes_Discs>? discList;

    public bool IsCompleteButtonDisabled => discList == null || discList.Count == 0;

    private bool IsEditable => Contribution?.Status is
        UserContributionStatus.Pending or
        UserContributionStatus.ChangesRequested or
        UserContributionStatus.Rejected;

    private bool isSaving;

    private IContributionDiscs_MyContributions_Nodes_Discs? draggedDisc;
    private IContributionDiscs_MyContributions_Nodes_Discs? dragOverDisc;

    protected override async Task OnInitializedAsync()
    {
        if (this.ContributionClient == null)
        {
            throw new Exception("Contribution Service was not injected");
        }

        var result = await this.ContributionClient.ContributionDiscs.ExecuteAsync(ContributionId!, this.CancellationToken);
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

    private void OnDragStart(IContributionDiscs_MyContributions_Nodes_Discs disc)
    {
        if (isSaving) return;
        draggedDisc = disc;
    }

    private void OnDragEnter(IContributionDiscs_MyContributions_Nodes_Discs disc)
    {
        if (draggedDisc == null || disc == draggedDisc || discList == null || isSaving) return;
        dragOverDisc = disc;

        int fromIndex = discList.IndexOf(draggedDisc);
        int toIndex = discList.IndexOf(disc);
        if (fromIndex < 0 || toIndex < 0) return;

        discList.RemoveAt(fromIndex);
        discList.Insert(toIndex, draggedDisc);
    }

    private async Task OnDragEnd()
    {
        var wasDragging = draggedDisc != null;
        draggedDisc = null;
        dragOverDisc = null;

        if (!wasDragging || isSaving) return;
        await SaveDiscOrder();
    }

    private string GetRowClass(IContributionDiscs_MyContributions_Nodes_Discs disc)
    {
        if (disc == draggedDisc) return "dragging";
        if (disc == dragOverDisc) return "drag-over";
        return string.Empty;
    }

    private string GetDiscTargetUrl(IContributionDiscs_MyContributions_Nodes_Discs disc)
    {
        if (!string.IsNullOrEmpty(disc.ExistingDiscPath))
        {
            return $"/contribution/{ContributionId}/disc/{disc.EncodedId}/edit?returnUrl=/contribution/{ContributionId}";
        }

        return $"/contribution/{ContributionId}/discs/{disc.EncodedId}/identify";
    }

    private string GetCopiedFromSummary(IContributionDiscs_MyContributions_Nodes_Discs disc)
    {
        if (string.IsNullOrEmpty(disc.ExistingDiscPath))
        {
            return string.Empty;
        }

        try
        {
            var (_, externalId, releaseSlug, discSlug) = TheDiscDb.Web.Data.UserContributionDisc.ParseDiscPath(disc.ExistingDiscPath);
            return $"{releaseSlug} / {discSlug} (TMDB {externalId})";
        }
        catch
        {
            return disc.ExistingDiscPath;
        }
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

            await this.ContributionClient.ReorderDiscs.ExecuteAsync(input, this.CancellationToken);
        }
        finally
        {
            isSaving = false;
        }
    }
}
