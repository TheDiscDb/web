using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using TheDiscDb.Services.EditSuggestions;
using TheDiscDb.Web.Data;

namespace TheDiscDb.Services;

public class MessageService(
    IDbContextFactory<SqlServerDataContext> dbFactory,
    IContributionNotificationService notificationService,
    IEditSuggestionNotificationService editSuggestionNotificationService,
    UserManager<TheDiscDbUser> userManager,
    ILogger<MessageService> logger) : IMessageService
{
    public async Task<UserMessage> SendAdminMessageAsync(int contributionId, string fromUserId, string toUserId, string message, CancellationToken cancellationToken = default)
    {
        ValidateMessage(message);
        await using var database = await dbFactory.CreateDbContextAsync(cancellationToken);

        var userMessage = new UserMessage
        {
            ContributionId = contributionId,
            FromUserId = fromUserId,
            ToUserId = toUserId,
            Message = message,
            IsRead = false,
            CreatedAt = DateTimeOffset.UtcNow,
            Type = UserMessageType.AdminMessage
        };

        database.UserMessages.Add(userMessage);
        await database.SaveChangesAsync(cancellationToken);

        try
        {
            var contribution = await database.UserContributions.FirstOrDefaultAsync(c => c.Id == contributionId, cancellationToken);
            if (contribution != null)
            {
                var recipient = await userManager.FindByIdAsync(toUserId);
                await notificationService.NotifyMessageFromAdminAsync(contribution, message, recipient?.Email, cancellationToken);
            }
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to send admin message notification for contribution {Id}", contributionId);
        }

        return userMessage;
    }

    public async Task<UserMessage> SendUserMessageAsync(int contributionId, string fromUserId, string message, CancellationToken cancellationToken = default)
    {
        ValidateMessage(message);
        await using var database = await dbFactory.CreateDbContextAsync(cancellationToken);

        // Find the most recent admin who messaged this contribution
        var lastAdminId = await database.UserMessages
            .Where(m => m.ContributionId == contributionId && m.Type == UserMessageType.AdminMessage)
            .OrderByDescending(m => m.CreatedAt)
            .Select(m => m.FromUserId)
            .FirstOrDefaultAsync(cancellationToken) ?? string.Empty;

        var userMessage = new UserMessage
        {
            ContributionId = contributionId,
            FromUserId = fromUserId,
            ToUserId = lastAdminId,
            Message = message,
            IsRead = false,
            CreatedAt = DateTimeOffset.UtcNow,
            Type = UserMessageType.UserMessage
        };

        database.UserMessages.Add(userMessage);
        await database.SaveChangesAsync(cancellationToken);

        try
        {
            var contribution = await database.UserContributions.FirstOrDefaultAsync(c => c.Id == contributionId, cancellationToken);
            if (contribution != null)
            {
                var sender = await userManager.FindByIdAsync(fromUserId);
                await notificationService.NotifyMessageFromUserAsync(contribution, message, sender?.UserName, sender?.Email, cancellationToken);
            }
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to send user message notification for contribution {Id}", contributionId);
        }

        return userMessage;
    }

    public async Task<UserMessage> SendAdminBoxsetMessageAsync(int boxsetId, string fromUserId, string toUserId, string message, CancellationToken cancellationToken = default)
    {
        ValidateMessage(message);
        await using var database = await dbFactory.CreateDbContextAsync(cancellationToken);

        var userMessage = new UserMessage
        {
            BoxsetId = boxsetId,
            FromUserId = fromUserId,
            ToUserId = toUserId,
            Message = message,
            IsRead = false,
            CreatedAt = DateTimeOffset.UtcNow,
            Type = UserMessageType.AdminMessage
        };

        database.UserMessages.Add(userMessage);
        await database.SaveChangesAsync(cancellationToken);

        // Notification service is contribution-shaped today; skip with a log so the message is
        // still persisted (which is what surfaces in the user's UI). A future enhancement could
        // teach the notification service about boxsets.
        logger.LogInformation("Persisted admin message for boxset {BoxsetId}; notifications for boxsets are not implemented.", boxsetId);

        return userMessage;
    }

    public async Task<UserMessage> SendUserBoxsetMessageAsync(int boxsetId, string fromUserId, string message, CancellationToken cancellationToken = default)
    {
        ValidateMessage(message);
        await using var database = await dbFactory.CreateDbContextAsync(cancellationToken);

        var lastAdminId = await database.UserMessages
            .Where(m => m.BoxsetId == boxsetId && m.Type == UserMessageType.AdminMessage)
            .OrderByDescending(m => m.CreatedAt)
            .Select(m => m.FromUserId)
            .FirstOrDefaultAsync(cancellationToken) ?? string.Empty;

        var userMessage = new UserMessage
        {
            BoxsetId = boxsetId,
            FromUserId = fromUserId,
            ToUserId = lastAdminId,
            Message = message,
            IsRead = false,
            CreatedAt = DateTimeOffset.UtcNow,
            Type = UserMessageType.UserMessage
        };

        database.UserMessages.Add(userMessage);
        await database.SaveChangesAsync(cancellationToken);

        logger.LogInformation("Persisted user message for boxset {BoxsetId}; notifications for boxsets are not implemented.", boxsetId);

        return userMessage;
    }

    public async Task<UserMessage> SendAdminEditSuggestionMessageAsync(
        int suggestionId,
        string fromUserId,
        string message,
        bool sendNotification = true,
        CancellationToken cancellationToken = default)
    {
        ValidateMessage(message);
        await using var database = await dbFactory.CreateDbContextAsync(cancellationToken);

        var suggestion = await database.EditSuggestions
            .FirstOrDefaultAsync(s => s.Id == suggestionId, cancellationToken)
            ?? throw new InvalidOperationException($"EditSuggestion {suggestionId} not found.");

        var userMessage = new UserMessage
        {
            EditSuggestionId = suggestionId,
            FromUserId = fromUserId,
            ToUserId = suggestion.UserId,
            Message = message,
            IsRead = false,
            CreatedAt = DateTimeOffset.UtcNow,
            Type = UserMessageType.AdminMessage,
        };

        database.UserMessages.Add(userMessage);
        await database.SaveChangesAsync(cancellationToken);

        if (sendNotification)
        {
            await NotifyAdminEditSuggestionMessageAsync(suggestion, message, cancellationToken);
        }

        return userMessage;
    }

    public async Task<UserMessage> SendUserEditSuggestionMessageAsync(
        int suggestionId,
        string fromUserId,
        string message,
        CancellationToken cancellationToken = default)
    {
        ValidateMessage(message);
        await using var database = await dbFactory.CreateDbContextAsync(cancellationToken);

        var suggestion = await database.EditSuggestions
            .FirstOrDefaultAsync(s => s.Id == suggestionId, cancellationToken)
            ?? throw new InvalidOperationException($"EditSuggestion {suggestionId} not found.");

        if (suggestion.UserId != fromUserId)
        {
            throw new UnauthorizedAccessException(
                $"User '{fromUserId}' is not the owner of suggestion {suggestionId}.");
        }

        var lastAdminId = await database.UserMessages
            .Where(m => m.EditSuggestionId == suggestionId && m.Type == UserMessageType.AdminMessage)
            .OrderByDescending(m => m.CreatedAt)
            .Select(m => m.FromUserId)
            .FirstOrDefaultAsync(cancellationToken);

        if (string.IsNullOrEmpty(lastAdminId))
        {
            throw new InvalidOperationException("An administrator must start the conversation before the suggester can reply.");
        }

        var userMessage = new UserMessage
        {
            EditSuggestionId = suggestionId,
            FromUserId = fromUserId,
            ToUserId = lastAdminId,
            Message = message,
            IsRead = false,
            CreatedAt = DateTimeOffset.UtcNow,
            Type = UserMessageType.UserMessage,
        };

        database.UserMessages.Add(userMessage);
        await database.SaveChangesAsync(cancellationToken);

        try
        {
            var sender = await userManager.FindByIdAsync(fromUserId);
            await editSuggestionNotificationService.NotifyMessageFromUserAsync(
                suggestion, message, sender?.UserName, sender?.Email, cancellationToken);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to send user message notification for edit suggestion {Id}", suggestionId);
        }

        return userMessage;
    }

    public async Task MarkEditSuggestionMessagesAsReadAsync(
        int suggestionId,
        string userId,
        CancellationToken cancellationToken = default)
    {
        await using var database = await dbFactory.CreateDbContextAsync(cancellationToken);
        var unreadMessages = await database.UserMessages
            .Where(message =>
                message.EditSuggestionId == suggestionId &&
                message.ToUserId == userId &&
                !message.IsRead)
            .ToListAsync(cancellationToken);

        foreach (var message in unreadMessages)
        {
            message.IsRead = true;
        }

        await database.SaveChangesAsync(cancellationToken);
    }

    public async Task NotifyAdminEditSuggestionMessageAsync(
        EditSuggestion suggestion,
        string message,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var recipient = await userManager.FindByIdAsync(suggestion.UserId);
            await editSuggestionNotificationService.NotifyMessageFromAdminAsync(
                suggestion, message, recipient?.Email, cancellationToken);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to send admin message notification for edit suggestion {Id}", suggestion.Id);
        }
    }

    private static void ValidateMessage(string message)
    {
        if (string.IsNullOrWhiteSpace(message) || message.Length > 10_000)
        {
            throw new ArgumentException("Message must be between 1 and 10,000 characters.", nameof(message));
        }
    }
}
