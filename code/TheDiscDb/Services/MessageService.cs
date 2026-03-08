using Microsoft.EntityFrameworkCore;
using TheDiscDb.Web.Data;

namespace TheDiscDb.Services;

public interface IMessageService
{
    Task<UserMessage> SendAdminMessageAsync(int contributionId, string fromUserId, string toUserId, string message, CancellationToken cancellationToken = default);
    Task<UserMessage> SendUserMessageAsync(int contributionId, string fromUserId, string message, CancellationToken cancellationToken = default);
}

public class MessageService(IDbContextFactory<SqlServerDataContext> dbFactory) : IMessageService
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

        return userMessage;
    }

    public async Task<UserMessage> SendUserMessageAsync(int contributionId, string fromUserId, string message, CancellationToken cancellationToken = default)
    {
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

        return userMessage;
    }
}
