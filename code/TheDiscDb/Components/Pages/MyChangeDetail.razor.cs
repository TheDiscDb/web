using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using TheDiscDb.Components.Controls;
using TheDiscDb.Services;
using TheDiscDb.Services.EditSuggestions;
using TheDiscDb.Services.Server;
using TheDiscDb.Web.Data;

namespace TheDiscDb.Components.Pages;

[Authorize]
public partial class MyChangeDetail : AuthenticatedComponentBase
{
    [Parameter]
    public string SuggestionSquid { get; set; } = null!;

    [Inject]
    private IEditSuggestionService SuggestionService { get; set; } = null!;

    [Inject]
    private IdEncoder IdEncoder { get; set; } = null!;

    [Inject]
    private ILogger<MyChangeDetail> Logger { get; set; } = null!;

    [Inject]
    private IDbContextFactory<SqlServerDataContext> DbFactory { get; set; } = null!;

    [Inject]
    private IMessageService MessageService { get; set; } = null!;

    private int suggestionId;
    private EditSuggestion? suggestion;
    private string? actionMessage;
    private bool actionSuccess;
    private bool isProcessing;
    private string newMessage = string.Empty;
    private bool canReply;
    private List<EditSuggestionTimelineEntry> timelineEntries = [];

    private static readonly IReadOnlyCollection<string> IdentityFields =
    [
        "mediaItemSlug", "boxsetSlug", "releaseSlug",
        "discSlug", "discIndex", "titleIndex",
        "chapterIndex", "trackIndex", "sourceFile", "hasItem",
    ];

    protected override async Task OnInitializedAsync()
    {
        suggestionId = IdEncoder.Decode(SuggestionSquid);
        await LoadSuggestion();
    }

    private async Task LoadSuggestion()
    {
        var loaded = await SuggestionService.GetByIdAsync(suggestionId, CancellationToken.None);

        // Only show if the user owns this suggestion
        var userId = await GetCurrentUserIdAsync();
        if (userId is not null && loaded?.UserId == userId)
        {
            suggestion = loaded;
            await MessageService.MarkEditSuggestionMessagesAsReadAsync(suggestionId, userId);
            await LoadTimelineAsync(userId);
        }
    }

    private async Task LoadTimelineAsync(string currentUserId)
    {
        await using var db = await DbFactory.CreateDbContextAsync();
        var history = await db.EditSuggestionHistory
            .AsNoTracking()
            .Where(item =>
                item.SuggestionId == suggestionId &&
                item.Type != EditSuggestionHistoryType.AdminMessage &&
                item.Type != EditSuggestionHistoryType.UserMessage)
            .ToListAsync();
        var messages = await db.UserMessages
            .AsNoTracking()
            .Where(message => message.EditSuggestionId == suggestionId)
            .ToListAsync();
        canReply = messages.Any(message => message.Type == UserMessageType.AdminMessage);

        timelineEntries =
        [
            ..history.Select(item => new EditSuggestionTimelineEntry(
                item.Type.ToString(),
                HistoryBadge(item.Type),
                item.TimeStamp,
                item.UserId == currentUserId ? "You" : "Administrator",
                item.Description)),
            ..messages.Select(message => new EditSuggestionTimelineEntry(
                message.Type == UserMessageType.AdminMessage ? "Admin message" : "Your message",
                message.Type == UserMessageType.AdminMessage ? "bg-primary" : "bg-secondary",
                message.CreatedAt,
                message.FromUserId == currentUserId ? "You" : "Administrator",
                message.Message)),
        ];
    }

    private async Task SendMessage()
    {
        if (string.IsNullOrWhiteSpace(newMessage))
        {
            return;
        }

        isProcessing = true;
        actionMessage = null;
        try
        {
            var userId = await GetCurrentUserIdAsync();
            if (userId is null)
            {
                return;
            }

            await MessageService.SendUserEditSuggestionMessageAsync(suggestionId, userId, newMessage);
            newMessage = string.Empty;
            actionMessage = "Message sent.";
            actionSuccess = true;
            await LoadSuggestion();
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Failed to send message for suggestion {SuggestionId}", suggestionId);
            actionMessage = ex is ArgumentException ? ex.Message : "The message could not be sent.";
            actionSuccess = false;
        }
        finally
        {
            isProcessing = false;
        }
    }

    private static string HistoryBadge(EditSuggestionHistoryType type) => type switch
    {
        EditSuggestionHistoryType.Created => "bg-success",
        EditSuggestionHistoryType.StatusChanged => "bg-info text-dark",
        EditSuggestionHistoryType.ChangeStatusChanged => "bg-warning text-dark",
        EditSuggestionHistoryType.FileSynced => "bg-success",
        EditSuggestionHistoryType.Withdrawn => "bg-secondary",
        _ => "bg-light text-dark",
    };

    private async Task WithdrawSuggestion()
    {
        isProcessing = true;
        actionMessage = null;

        try
        {
            var userId = await GetCurrentUserIdAsync();
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

    private static string GetStatusBadge(EditSuggestionStatus status) => status switch
    {
        EditSuggestionStatus.Pending => "bg-warning text-dark",
        EditSuggestionStatus.InReview => "bg-info text-dark",
        EditSuggestionStatus.ChangesRequested => "bg-warning text-dark",
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

    private bool CanWithdraw =>
        suggestion is not null &&
        suggestion.Status is (
            EditSuggestionStatus.Pending
            or EditSuggestionStatus.InReview
            or EditSuggestionStatus.ChangesRequested) &&
        !suggestion.Changes.Any(change =>
            change.Status is EditSuggestionChangeStatus.Approved or EditSuggestionChangeStatus.Applied);
}
