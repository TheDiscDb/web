using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Logging;
using TheDiscDb.Services.EditSuggestions;
using TheDiscDb.Services.Server;
using TheDiscDb.Web.Data;

namespace TheDiscDb.Components.Pages;

[Authorize]
public partial class MyChangeDetail : ComponentBase
{
    [Parameter]
    public string SuggestionSquid { get; set; } = null!;

    [Inject]
    private IEditSuggestionService SuggestionService { get; set; } = null!;

    [Inject]
    private IdEncoder IdEncoder { get; set; } = null!;

    [Inject]
    private AuthenticationStateProvider AuthStateProvider { get; set; } = null!;

    [Inject]
    private UserManager<TheDiscDbUser> UserManager { get; set; } = null!;

    [Inject]
    private ILogger<MyChangeDetail> Logger { get; set; } = null!;

    private int suggestionId;
    private EditSuggestion? suggestion;
    private string? actionMessage;
    private bool actionSuccess;
    private bool isProcessing;

    protected override async Task OnInitializedAsync()
    {
        suggestionId = IdEncoder.Decode(SuggestionSquid);
        await LoadSuggestion();
    }

    private async Task LoadSuggestion()
    {
        var loaded = await SuggestionService.GetByIdAsync(suggestionId, CancellationToken.None);

        // Only show if the user owns this suggestion
        var userId = await GetUserId();
        if (loaded?.UserId == userId)
        {
            suggestion = loaded;
        }
    }

    private async Task WithdrawSuggestion()
    {
        isProcessing = true;
        actionMessage = null;

        try
        {
            var userId = await GetUserId();
            if (userId == null) return;

            var result = await SuggestionService.WithdrawAsync(suggestionId, userId, isAdmin: false, CancellationToken.None);
            if (result == null)
            {
                actionMessage = "Unable to withdraw this suggestion.";
                actionSuccess = false;
            }
            else
            {
                actionMessage = "Suggestion withdrawn.";
                actionSuccess = true;
            }

            await LoadSuggestion();
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Failed to withdraw suggestion {SuggestionId}", suggestionId);
            actionMessage = "An error occurred while withdrawing.";
            actionSuccess = false;
        }
        finally
        {
            isProcessing = false;
        }
    }

    private async Task<string?> GetUserId()
    {
        var authState = await AuthStateProvider.GetAuthenticationStateAsync();
        return UserManager.GetUserId(authState.User);
    }

    private static string GetStatusBadge(EditSuggestionStatus status) => status switch
    {
        EditSuggestionStatus.Pending => "bg-warning text-dark",
        EditSuggestionStatus.InReview => "bg-info text-dark",
        EditSuggestionStatus.Approved => "bg-success",
        EditSuggestionStatus.PartiallyApproved => "bg-success",
        EditSuggestionStatus.Rejected => "bg-danger",
        EditSuggestionStatus.Conflicted => "bg-danger",
        EditSuggestionStatus.Withdrawn => "bg-secondary",
        _ => "bg-light text-dark",
    };

    private static string GetChangeStatusBadge(EditSuggestionChangeStatus status) => status switch
    {
        EditSuggestionChangeStatus.Pending => "bg-warning text-dark",
        EditSuggestionChangeStatus.Approved => "bg-info",
        EditSuggestionChangeStatus.Applied => "bg-success",
        EditSuggestionChangeStatus.Rejected => "bg-danger",
        EditSuggestionChangeStatus.Conflicted => "bg-danger",
        _ => "bg-light text-dark",
    };
}
