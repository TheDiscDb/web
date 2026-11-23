using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Components;
using StrawberryShake;
using TheDiscDb.Client.Contributions;
using TheDiscDb.Client.Controls;

namespace TheDiscDb.Client.Pages.Contribute;

[Authorize]
public partial class BoxsetMessages : MessageConversationBase
{
    [Parameter]
    public string? BoxsetId { get; set; }

    protected override string? ThreadId => this.BoxsetId;
    protected override (string Text, string Url)[] Breadcrumbs => [
        ("Messages", "/messages"),
        ("Boxset", $"/contribution/boxset/{this.BoxsetId}")
    ];

    protected override async Task<MessagePage> GetPage(string threadId, string? after, CancellationToken cancellationToken)
    {
        var result = await this.ContributionClient.GetBoxsetChat.ExecuteAsync(threadId, 100, after, cancellationToken);
        if (!result.IsSuccessResult() || result.Data?.BoxsetChat == null)
            throw new InvalidOperationException("Failed to load messages.");

        var chat = result.Data.BoxsetChat;
        return new MessagePage(chat.Nodes?.Select(m => new ConversationMessage(m.Id, m.FromUserId, m.ToUserId, m.Message, m.IsRead, m.CreatedAt, m.Type)).ToList() ?? [],
            chat.PageInfo.HasNextPage, chat.PageInfo.EndCursor, result.Data.CurrentUserId);
    }

    protected override async Task<bool> Acknowledge(string threadId, int[] messageIds, CancellationToken cancellationToken)
    {
        var result = await this.ContributionClient.MarkBoxsetMessagesAsRead.ExecuteAsync(new MarkBoxsetMessagesAsReadInput
        {
            BoxsetId = threadId,
            MessageIds = messageIds
        }, cancellationToken);
        return result.IsSuccessResult() && result.Data?.MarkBoxsetMessagesAsRead?.Boolean == true &&
            !(result.Data.MarkBoxsetMessagesAsRead.Errors?.Any() ?? false);
    }

    protected override async Task<string?> Send(string threadId, string message, CancellationToken cancellationToken)
    {
        var result = await this.ContributionClient.SendBoxsetUserMessage.ExecuteAsync(new SendBoxsetUserMessageInput
        {
            BoxsetId = threadId,
            Message = message
        }, cancellationToken);
        if (result.Data?.SendBoxsetUserMessage?.Errors?.FirstOrDefault() is { } error)
        {
            return error switch
            {
                ISendBoxsetUserMessage_SendBoxsetUserMessage_Errors_BoxsetNotFoundError e => e.Message,
                ISendBoxsetUserMessage_SendBoxsetUserMessage_Errors_AuthenticationError e => e.Message,
                ISendBoxsetUserMessage_SendBoxsetUserMessage_Errors_InvalidIdError e => e.Message,
                ISendBoxsetUserMessage_SendBoxsetUserMessage_Errors_InvalidOwnershipError e => e.Message,
                _ => "An unexpected error occurred."
            };
        }
        return result.IsSuccessResult() && result.Data?.SendBoxsetUserMessage != null ? null : "Failed to send message.";
    }
}