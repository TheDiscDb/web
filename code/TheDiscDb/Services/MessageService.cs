using System.Security.Claims;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using TheDiscDb.GraphQL.Contribute.Exceptions;
using TheDiscDb.Web.Data;

namespace TheDiscDb.Services;

public interface IMessageService
{
    Task<UserMessage> SendAdminMessageAsync(int contributionId, string fromUserId, string toUserId, string message, CancellationToken cancellationToken = default);
    Task<UserMessage> SendUserMessageAsync(int contributionId, string fromUserId, string message, CancellationToken cancellationToken = default);
    Task<UserMessage> SendAdminBoxsetMessageAsync(int boxsetId, string fromUserId, string toUserId, string message, CancellationToken cancellationToken = default);
    Task<UserMessage> SendUserBoxsetMessageAsync(int boxsetId, string fromUserId, string message, CancellationToken cancellationToken = default);
    Task MarkContributionMessagesAsReadAsync(int contributionId, ClaimsPrincipal user, IReadOnlyCollection<int>? messageIds, CancellationToken cancellationToken = default);
    Task MarkBoxsetMessagesAsReadAsync(int boxsetId, ClaimsPrincipal user, IReadOnlyCollection<int>? messageIds, CancellationToken cancellationToken = default);
}

public class MessageService(
    IDbContextFactory<SqlServerDataContext> dbFactory,
    IContributionNotificationService notificationService,
    UserManager<TheDiscDbUser> userManager,
    ILogger<MessageService> logger) : IMessageService
{
    public async Task<UserMessage> SendAdminMessageAsync(int contributionId, string fromUserId, string toUserId, string message, CancellationToken cancellationToken = default)
    {
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
        await using var database = await dbFactory.CreateDbContextAsync(cancellationToken);

        var userMessage = new UserMessage
        {
            ContributionId = contributionId,
            FromUserId = fromUserId,
            ToUserId = string.Empty,
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
        await using var database = await dbFactory.CreateDbContextAsync(cancellationToken);

        var userMessage = new UserMessage
        {
            BoxsetId = boxsetId,
            FromUserId = fromUserId,
            ToUserId = string.Empty,
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

    public Task MarkContributionMessagesAsReadAsync(int contributionId, ClaimsPrincipal user, IReadOnlyCollection<int>? messageIds, CancellationToken cancellationToken = default) =>
        this.MarkMessagesAsReadAsync(contributionId, false, user, messageIds, cancellationToken);

    public Task MarkBoxsetMessagesAsReadAsync(int boxsetId, ClaimsPrincipal user, IReadOnlyCollection<int>? messageIds, CancellationToken cancellationToken = default) =>
        this.MarkMessagesAsReadAsync(boxsetId, true, user, messageIds, cancellationToken);

    private async Task MarkMessagesAsReadAsync(int threadId, bool isBoxset, ClaimsPrincipal user, IReadOnlyCollection<int>? messageIds, CancellationToken cancellationToken)
    {
        var userId = user.FindFirstValue(ClaimTypes.NameIdentifier);
        if (user.Identity?.IsAuthenticated != true || string.IsNullOrEmpty(userId))
        {
            throw new AuthenticationException("UserId not found");
        }

        var threadType = isBoxset ? "Boxset" : "Contribution";
        if (threadId <= 0)
        {
            throw new InvalidIdException(threadId.ToString(), threadType);
        }

        await using var database = await dbFactory.CreateDbContextAsync(cancellationToken);
        var ownerId = isBoxset
            ? await database.UserContributionBoxsets.Where(b => b.Id == threadId).Select(b => b.UserId).FirstOrDefaultAsync(cancellationToken)
            : await database.UserContributions.Where(c => c.Id == threadId).Select(c => c.UserId).FirstOrDefaultAsync(cancellationToken);

        if (ownerId == null)
        {
            if (isBoxset)
                throw new BoxsetNotFoundException(threadId.ToString());
            throw new ContributionNotFoundException(threadId.ToString());
        }

        if (!user.IsInRole(DefaultRoles.Administrator) && ownerId != userId)
        {
            throw new InvalidOwnershipException(threadId.ToString(), threadType);
        }

        // Older clients omit IDs. Never let that acknowledge messages they have not displayed.
        if (messageIds == null || messageIds.Count == 0)
        {
            return;
        }

        var ids = messageIds.Distinct().ToArray();
        var thread = database.UserMessages.Where(m =>
            isBoxset ? m.BoxsetId == threadId : m.ContributionId == threadId);
        var unread = await MessageAccess.Inbox(thread, user)
            .Where(e => e.IsUnread && ids.Contains(e.Message.Id))
            .Select(e => e.Message)
            .ToListAsync(cancellationToken);

        foreach (var message in unread)
        {
            message.IsRead = true;
        }

        await database.SaveChangesAsync(cancellationToken);
    }
}