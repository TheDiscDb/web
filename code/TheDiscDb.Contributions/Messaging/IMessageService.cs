namespace TheDiscDb.Services;

using TheDiscDb.Web.Data;

public interface IMessageService
{
    Task<UserMessage> SendAdminMessageAsync(
        int contributionId,
        string fromUserId,
        string toUserId,
        string message,
        CancellationToken cancellationToken = default);

    Task<UserMessage> SendUserMessageAsync(
        int contributionId,
        string fromUserId,
        string message,
        CancellationToken cancellationToken = default);

    Task<UserMessage> SendAdminBoxsetMessageAsync(
        int boxsetId,
        string fromUserId,
        string toUserId,
        string message,
        CancellationToken cancellationToken = default);

    Task<UserMessage> SendUserBoxsetMessageAsync(
        int boxsetId,
        string fromUserId,
        string message,
        CancellationToken cancellationToken = default);

    Task<UserMessage> SendAdminEditSuggestionMessageAsync(
        int suggestionId,
        string fromUserId,
        string message,
        bool sendNotification = true,
        CancellationToken cancellationToken = default);

    Task<UserMessage> SendUserEditSuggestionMessageAsync(
        int suggestionId,
        string fromUserId,
        string message,
        CancellationToken cancellationToken = default);

    Task MarkEditSuggestionMessagesAsReadAsync(
        int suggestionId,
        string userId,
        CancellationToken cancellationToken = default);

    Task NotifyAdminEditSuggestionMessageAsync(
        EditSuggestion suggestion,
        string message,
        CancellationToken cancellationToken = default);
}
