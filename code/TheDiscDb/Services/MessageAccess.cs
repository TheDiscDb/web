using System.Security.Claims;
using TheDiscDb.Web.Data;

namespace TheDiscDb.Services;

public static class MessageAccess
{
    public static IQueryable<UserMessage> VisibleTo(IQueryable<UserMessage> messages, ClaimsPrincipal user)
    {
        var userId = user.FindFirstValue(ClaimTypes.NameIdentifier);
        if (user.Identity?.IsAuthenticated != true || string.IsNullOrEmpty(userId))
        {
            return messages.Where(m => false);
        }

        messages = messages.Where(m =>
            (m.ContributionId != null && m.BoxsetId == null) ||
            (m.ContributionId == null && m.BoxsetId != null));

        if (user.IsInRole(DefaultRoles.Administrator))
        {
            return messages;
        }

        return messages.Where(m =>
            (m.Contribution != null && m.Contribution.UserId == userId) ||
            (m.Boxset != null && m.Boxset.UserId == userId));
    }

    public static IQueryable<MessageInboxEntry> Inbox(IQueryable<UserMessage> messages, ClaimsPrincipal user)
    {
        var userId = user.FindFirstValue(ClaimTypes.NameIdentifier);
        var isAdmin = user.IsInRole(DefaultRoles.Administrator);
        return VisibleTo(messages, user).Select(m => new MessageInboxEntry
        {
            Message = m,
            IsUnread = !m.IsRead &&
                ((isAdmin && m.Type == UserMessageType.UserMessage) ||
                 (m.Type == UserMessageType.AdminMessage && m.ToUserId == userId))
        });
    }
}

public class MessageInboxEntry
{
    public UserMessage Message { get; set; } = null!;
    public bool IsUnread { get; set; }
}