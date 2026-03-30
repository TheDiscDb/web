using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Components;
using StrawberryShake;
using TheDiscDb.Client.Contributions;

namespace TheDiscDb.Client.Pages.Contribute;

[Authorize]
public partial class BoxsetContribution : ComponentBase
{
    [Parameter]
    public string BoxsetId { get; set; } = string.Empty;

    [Inject]
    IContributionClient ContributionClient { get; set; } = null!;

    [Inject]
    NavigationManager Navigation { get; set; } = null!;

    private IGetBoxsetDetail_MyBoxsets_Nodes? Boxset;
    private List<IGetBoxsetDetail_MyBoxsets_Nodes_Members> Members = new();
    private bool isLoading = true;
    private string? errorMessage;

    private bool showRemoveDialog;
    private IGetBoxsetDetail_MyBoxsets_Nodes_Members? removingMember;

    private bool CanModify => Boxset?.Status == UserContributionStatus.Pending;
    private int TotalDiscCount => Members.Count;

    protected override async Task OnInitializedAsync()
    {
        await LoadBoxsetAsync();
    }

    private async Task LoadBoxsetAsync()
    {
        isLoading = true;
        errorMessage = null;

        try
        {
            var filter = new UserContributionBoxsetFilterInput
            {
                EncodedId = new EncodedIdOperationFilterInput { Eq = BoxsetId }
            };

            var result = await ContributionClient.GetBoxsetDetail.ExecuteAsync(filter);
            if (result != null && result.IsSuccessResult())
            {
                Boxset = result.Data?.MyBoxsets?.Nodes?.FirstOrDefault();
                Members = Boxset?.Members?.ToList() ?? new();
            }
        }
        catch (Exception)
        {
            errorMessage = "Failed to load boxset. Please try again.";
        }
        finally
        {
            isLoading = false;
        }
    }

    private async Task MoveUp(IGetBoxsetDetail_MyBoxsets_Nodes_Members member)
    {
        var currentIndex = Members.FindIndex(m => m.SortOrder == member.SortOrder);
        if (currentIndex <= 0) return;

        await ReorderMembers(currentIndex, currentIndex - 1);
    }

    private async Task MoveDown(IGetBoxsetDetail_MyBoxsets_Nodes_Members member)
    {
        var currentIndex = Members.FindIndex(m => m.SortOrder == member.SortOrder);
        if (currentIndex >= Members.Count - 1) return;

        await ReorderMembers(currentIndex, currentIndex + 1);
    }

    private async Task ReorderMembers(int fromIndex, int toIndex)
    {
        var ordered = Members.OrderBy(m => m.SortOrder).ToList();
        var item = ordered[fromIndex];
        ordered.RemoveAt(fromIndex);
        ordered.Insert(toIndex, item);

        var contributionIds = ordered.Select(m => m.Disc.EncodedId).ToList();

        try
        {
            var result = await ContributionClient.ReorderBoxsetMembers.ExecuteAsync(new ReorderBoxsetMembersInput
            {
                BoxsetId = BoxsetId,
                DiscIds = contributionIds
            });

            if (result.Data?.ReorderBoxsetMembers?.Errors is { Count: > 0 } errors)
            {
                var error = errors[0];
                errorMessage = error.Code ?? "Failed to reorder.";
                return;
            }

            await LoadBoxsetAsync();
        }
        catch (Exception)
        {
            errorMessage = "Failed to reorder. Please try again.";
        }
    }

    private void ShowAddDialog()
    {
        Navigation.NavigateTo($"/contribution/boxset/{BoxsetId}/add");
    }

    private void ConfirmRemove(IGetBoxsetDetail_MyBoxsets_Nodes_Members member)
    {
        removingMember = member;
        showRemoveDialog = true;
    }

    private void CancelRemove()
    {
        showRemoveDialog = false;
        removingMember = null;
    }

    private async Task ExecuteRemove()
    {
        if (removingMember == null) return;

        try
        {
            var result = await ContributionClient.RemoveDiscFromBoxset.ExecuteAsync(
                new RemoveDiscFromBoxsetInput
                {
                    BoxsetId = BoxsetId,
                    DiscId = removingMember.Disc.EncodedId
                });

            if (result.Data?.RemoveDiscFromBoxset?.Errors is { Count: > 0 } errors)
            {
                var error = errors[0];
                errorMessage = error.Code ?? "Failed to remove contribution.";
                return;
            }

            showRemoveDialog = false;
            removingMember = null;
            await LoadBoxsetAsync();
        }
        catch (Exception)
        {
            errorMessage = "Failed to remove contribution. Please try again.";
        }
    }
}
