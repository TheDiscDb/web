using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Components;
using StrawberryShake;
using Syncfusion.Blazor.Inputs;
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

    [Inject]
    HttpClient HttpClient { get; set; } = null!;

    private IGetBoxsetDetail_MyBoxsets_Nodes? Boxset;
    private List<IGetBoxsetDetail_MyBoxsets_Nodes_Members> Members = new();
    private bool isLoading = true;
    private string? errorMessage;

    private bool showRemoveDialog;
    private IGetBoxsetDetail_MyBoxsets_Nodes_Members? removingMember;

    private bool CanModify => Boxset?.Status.IsEditableByOwner() ?? false;
    private int TotalDiscCount => Members.Count;

    private static string GetStatusBadgeClass(UserContributionStatus status) => status switch
    {
        UserContributionStatus.Pending => "bg-info",
        UserContributionStatus.ReadyForReview => "bg-primary",
        UserContributionStatus.Approved => "bg-success",
        UserContributionStatus.ChangesRequested => "bg-warning text-dark",
        UserContributionStatus.Rejected => "bg-danger",
        UserContributionStatus.Imported => "bg-secondary",
        _ => "bg-secondary"
    };

    private IGetBoxsetDetail_MyBoxsets_Nodes_Members? draggedMember;
    private IGetBoxsetDetail_MyBoxsets_Nodes_Members? dragOverMember;
    private bool isReorderSaving;

    // Image upload
    private string? currentFrontImageUrl;
    private string? currentBackImageUrl;
    private SfUploader? frontImageUploader;
    private SfUploader? backImageUploader;
    private bool imageUpdatePending;
    private long imageVersion;

    private string frontImageUploadUrl => $"/api/contribute/boxset/{BoxsetId}/images/front/upload";
    private string backImageUploadUrl => $"/api/contribute/boxset/{BoxsetId}/images/back/upload";

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
                Members = Boxset?.Members?.OrderBy(m => m.SortOrder).ToList() ?? new();
                currentFrontImageUrl = Boxset?.FrontImageUrl;
                currentBackImageUrl = Boxset?.BackImageUrl;
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

    private static string GetDirectImageUrl(string url, long version)
    {
        var separator = url.Contains('?') ? '&' : '?';
        return $"{url}{separator}v={version}";
    }

    private void OnDragStart(IGetBoxsetDetail_MyBoxsets_Nodes_Members member)
    {
        if (isReorderSaving || !CanModify) return;
        draggedMember = member;
    }

    private void OnDragEnter(IGetBoxsetDetail_MyBoxsets_Nodes_Members member)
    {
        if (draggedMember == null || member == draggedMember || isReorderSaving) return;
        dragOverMember = member;

        int fromIndex = Members.IndexOf(draggedMember);
        int toIndex = Members.IndexOf(member);
        if (fromIndex < 0 || toIndex < 0) return;

        Members.RemoveAt(fromIndex);
        Members.Insert(toIndex, draggedMember);
    }

    private async Task OnDragEnd()
    {
        var wasDragging = draggedMember != null;
        draggedMember = null;
        dragOverMember = null;

        if (!wasDragging || isReorderSaving) return;
        await SaveMemberOrder();
    }

    private string GetRowClass(IGetBoxsetDetail_MyBoxsets_Nodes_Members member)
    {
        if (member == draggedMember) return "dragging";
        if (member == dragOverMember) return "drag-over";
        return string.Empty;
    }

    private async Task SaveMemberOrder()
    {
        if (Members.Count == 0) return;

        isReorderSaving = true;
        errorMessage = null;
        try
        {
            var result = await ContributionClient.ReorderBoxsetMembers.ExecuteAsync(new ReorderBoxsetMembersInput
            {
                BoxsetId = BoxsetId,
                MemberIds = Members.Select(m => m.Id).ToList()
            });

            if (result.Data?.ReorderBoxsetMembers?.Errors is { Count: > 0 } errors)
            {
                errorMessage = errors[0].Code ?? "Failed to reorder.";
            }
        }
        catch (Exception)
        {
            errorMessage = "Failed to reorder. Please try again.";
        }
        finally
        {
            isReorderSaving = false;
        }
    }

    private void ShowAddDialog()
    {
        Navigation.NavigateTo($"/contribution/boxset/{BoxsetId}/add");
    }

    private void ShowAddExistingDialog()
    {
        Navigation.NavigateTo($"/contribution/boxset/{BoxsetId}/add-existing");
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
            if (removingMember.Disc != null)
            {
                var result = await ContributionClient.RemoveDiscFromBoxset.ExecuteAsync(
                    new RemoveDiscFromBoxsetInput
                    {
                        BoxsetId = BoxsetId,
                        DiscId = removingMember.Disc.EncodedId
                    });

                if (result.Data?.RemoveDiscFromBoxset?.Errors is { Count: > 0 } errors)
                {
                    errorMessage = errors[0].Code ?? "Failed to remove disc.";
                    return;
                }
            }
            else
            {
                var result = await ContributionClient.RemoveBoxsetMember.ExecuteAsync(
                    new RemoveBoxsetMemberInput
                    {
                        BoxsetId = BoxsetId,
                        MemberId = removingMember.Id
                    });

                if (result.Data?.RemoveBoxsetMember?.Errors is { Count: > 0 } errors)
                {
                    errorMessage = errors[0].Code ?? "Failed to remove disc.";
                    return;
                }
            }

            showRemoveDialog = false;
            removingMember = null;
            await LoadBoxsetAsync();
        }
        catch (Exception)
        {
            errorMessage = "Failed to remove disc. Please try again.";
        }
    }

    #region Image Upload Handlers

    private void FrontImageUploadSuccess(SuccessEventArgs args)
    {
        imageVersion = DateTimeOffset.UtcNow.Ticks;
        currentFrontImageUrl = $"/api/contribute/images/Contributions/Boxsets/{BoxsetId}/front.jpg";
        imageUpdatePending = true;
        StateHasChanged();
    }

    private void FrontImageUploadFailure(FailureEventArgs args)
    {
        errorMessage = $"Failed to upload front image: {args.Response}";
        StateHasChanged();
    }

    private void BackImageUploadSuccess(SuccessEventArgs args)
    {
        imageVersion = DateTimeOffset.UtcNow.Ticks;
        currentBackImageUrl = $"/api/contribute/images/Contributions/Boxsets/{BoxsetId}/back.jpg";
        imageUpdatePending = true;
        StateHasChanged();
    }

    private void BackImageUploadFailure(FailureEventArgs args)
    {
        errorMessage = $"Failed to upload back image: {args.Response}";
        StateHasChanged();
    }

    private async Task DeleteFrontImage()
    {
        errorMessage = null;
        try
        {
            var response = await HttpClient.PostAsync($"/api/contribute/boxset/{BoxsetId}/images/front/delete", null);
            if (response.IsSuccessStatusCode)
            {
                currentFrontImageUrl = null;
                if (frontImageUploader != null)
                {
                    await frontImageUploader.ClearAllAsync();
                }
            }
            else
            {
                errorMessage = "Failed to delete front image.";
            }
        }
        catch (Exception ex)
        {
            errorMessage = $"Failed to delete front image: {ex.Message}";
        }
    }

    private async Task DeleteBackImage()
    {
        errorMessage = null;
        try
        {
            var response = await HttpClient.PostAsync($"/api/contribute/boxset/{BoxsetId}/images/back/delete", null);
            if (response.IsSuccessStatusCode)
            {
                currentBackImageUrl = null;
                if (backImageUploader != null)
                {
                    await backImageUploader.ClearAllAsync();
                }
            }
            else
            {
                errorMessage = "Failed to delete back image.";
            }
        }
        catch (Exception ex)
        {
            errorMessage = $"Failed to delete back image: {ex.Message}";
        }
    }

    #endregion
}
