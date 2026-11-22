using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Components;
using StrawberryShake;
using TheDiscDb.Client.Contributions;
using TheDiscDb.Client.Controls;

namespace TheDiscDb.Client.Pages.Contribute;

[Authorize]
public partial class ContributionMessages : MessageConversationBase
{
    [Parameter]
    public string? ContributionId { get; set; }

    protected override string? ThreadId => this.ContributionId;
    protected override (string Text, string Url)[] Breadcrumbs => [
        ("Messages", "/messages"),
        ("Contribution", $"/contribution/{this.ContributionId}")
    ];

    protected override async Task<MessagePage> GetPage(string threadId, string? after, CancellationToken cancellationToken)
    {
        var result = await this.ContributionClient.GetContributionChat.ExecuteAsync(threadId, 100, after, cancellationToken);
        if (!result.IsSuccessResult() || result.Data?.ContributionChat == null)
            throw new InvalidOperationException("Failed to load messages.");

        var chat = result.Data.ContributionChat;
        return new MessagePage(chat.Nodes?.Select(m => new ConversationMessage(m.Id, m.FromUserId, m.ToUserId, m.Message, m.IsRead, m.CreatedAt, m.Type)).ToList() ?? [],
            chat.PageInfo.HasNextPage, chat.PageInfo.EndCursor, result.Data.CurrentUserId);
    }

    protected override async Task<bool> Acknowledge(string threadId, int[] messageIds, CancellationToken cancellationToken)
    {
        var result = await this.ContributionClient.MarkMessagesAsRead.ExecuteAsync(new MarkMessagesAsReadInput
        {
            ContributionId = threadId,
            MessageIds = messageIds
        }, cancellationToken);
        return result.IsSuccessResult() && result.Data?.MarkMessagesAsRead?.Boolean == true &&
            !(result.Data.MarkMessagesAsRead.Errors?.Any() ?? false);
    }

    protected override async Task<string?> Send(string threadId, string message, CancellationToken cancellationToken)
    {
        var result = await this.ContributionClient.SendUserMessage.ExecuteAsync(new SendUserMessageInput
        {
            ContributionId = threadId,
            Message = message
        }, cancellationToken);
        if (result.Data?.SendUserMessage?.Errors?.FirstOrDefault() is { } error)
        {
            return error switch
            {
                ISendUserMessage_SendUserMessage_Errors_ContributionNotFoundError e => e.Message,
                ISendUserMessage_SendUserMessage_Errors_AuthenticationError e => e.Message,
                ISendUserMessage_SendUserMessage_Errors_InvalidOwnershipError e => e.Message,
                _ => "An unexpected error occurred."
            };
        }
        return result.IsSuccessResult() && result.Data?.SendUserMessage != null ? null : "Failed to send message.";
    }
}