using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;
using StrawberryShake;
using TheDiscDb.Client.Contributions;
using TheDiscDb.Client.Interop;

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

    [Inject]
    public IJSRuntime JS { get; set; } = default!;

    private IContributionDiscs_MyContributions_Nodes? Contribution { get; set; }

    private List<IContributionDiscs_MyContributions_Nodes_Discs>? discList;

    public bool IsCompleteButtonDisabled => discList == null || discList.Count == 0;

    private bool IsEditable => Contribution?.Status is
        UserContributionStatus.Pending or
        UserContributionStatus.ChangesRequested or
        UserContributionStatus.Rejected;

    private bool isSaving;
    private bool showDeleteDiscDialog;
    private bool isDeletingDisc;
    private string? deleteDiscErrorMessage;
    private IContributionDiscs_MyContributions_Nodes_Discs? deletingDisc;

    private IContributionDiscs_MyContributions_Nodes_Discs? draggedDisc;
    private IContributionDiscs_MyContributions_Nodes_Discs? dragOverDisc;

    private ElementReference tableBodyRef;
    private TouchSortable<IContributionDiscs_MyContributions_Nodes_Discs>? sortable;

    protected override async Task OnInitializedAsync()
    {
        if (this.ContributionClient == null)
        {
            throw new Exception("Contribution Service was not injected");
        }

        await LoadContributionAsync();
    }

    private async Task LoadContributionAsync()
    {
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

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        // The drag handle (and reorder) only exist while the contribution is
        // editable. Bind the shared touch sortable to the table body so dragging
        // works on touch devices, mirroring the native (mouse) drag handlers.
        if (discList == null || discList.Count == 0 || !IsEditable)
        {
            return;
        }

        sortable ??= new TouchSortable<IContributionDiscs_MyContributions_Nodes_Discs>(
            JS,
            () => discList ?? (IReadOnlyList<IContributionDiscs_MyContributions_Nodes_Discs>)Array.Empty<IContributionDiscs_MyContributions_Nodes_Discs>(),
            OnDragStart,
            OnDragEnter,
            OnDragEnd,
            StateHasChanged);
        await sortable.InitAsync(tableBodyRef);
    }

    private string GetDiscTargetUrl(IContributionDiscs_MyContributions_Nodes_Discs disc)
    {
        if (!string.IsNullOrEmpty(disc.ExistingDiscPath))
        {
            return $"/contribution/{ContributionId}/disc/{disc.EncodedId}/edit?returnUrl=/contribution/{ContributionId}";
        }

        return $"/contribution/{ContributionId}/discs/{disc.EncodedId}/identify";
    }

    private string GetNamingHelperUrl(IContributionDiscs_MyContributions_Nodes_Discs disc)
    {
        return $"/contribution/{ContributionId}/discs/{disc.EncodedId}/naming?popup=1";
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

    private void ConfirmDeleteDisc(IContributionDiscs_MyContributions_Nodes_Discs disc)
    {
        deletingDisc = disc;
        deleteDiscErrorMessage = null;
        showDeleteDiscDialog = true;
    }

    private void CancelDeleteDisc()
    {
        showDeleteDiscDialog = false;
        deletingDisc = null;
        deleteDiscErrorMessage = null;
    }

    private async Task ExecuteDeleteDisc()
    {
        if (deletingDisc == null)
        {
            return;
        }

        isDeletingDisc = true;
        deleteDiscErrorMessage = null;
        try
        {
            var result = await this.ContributionClient.DeleteDiscFromContribution.ExecuteAsync(new DeleteDiscFromContributionInput
            {
                ContributionId = ContributionId!,
                DiscId = deletingDisc.EncodedId
            }, this.CancellationToken);

            if (result.Data?.DeleteDiscFromContribution?.Errors is { Count: > 0 } errors)
            {
                deleteDiscErrorMessage = errors[0].Code ?? "Failed to delete disc.";
                return;
            }

            showDeleteDiscDialog = false;
            deletingDisc = null;
            await LoadContributionAsync();
        }
        catch (Exception)
        {
            deleteDiscErrorMessage = "Failed to delete disc. Please try again.";
        }
        finally
        {
            isDeletingDisc = false;
        }
    }

    public override void Dispose()
    {
        sortable?.Dispose();
        base.Dispose();
    }
}
