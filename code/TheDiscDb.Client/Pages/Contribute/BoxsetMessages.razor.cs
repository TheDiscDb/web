using Markdig;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Components;
using StrawberryShake;
using TheDiscDb.Client.Contributions;

namespace TheDiscDb.Client.Pages.Contribute;

[Authorize]
public partial class BoxsetMessages : CancellableComponentBase
{
    [Parameter]
    public string? BoxsetId { get; set; }

    [Inject]
    private IContributionClient ContributionClient { get; set; } = null!;

    [Inject]
    private NavigationManager Navigation { get; set; } = null!;

    private List<IGetBoxsetChat_BoxsetChat_Nodes>? chatItems;
    private bool isLoading = true;
    private string? loadError;
    private string newMessage = string.Empty;
    private string? sendError;
    private bool isSending;
    private bool showPreview;
    private (string Text, string Url)[] breadcrumbItems = [];

    protected override async Task OnInitializedAsync()
    {
        breadcrumbItems = [
            BreadCrumbHelper.GetRootContributionLink(),
            (Text: "Boxset", Url: $"/contribution/boxset/{BoxsetId}")
        ];

        if (string.IsNullOrEmpty(BoxsetId))
        {
            loadError = "Invalid boxset ID.";
            isLoading = false;
            return;
        }

        await LoadMessages();
    }

    private async Task LoadMessages()
    {
        isLoading = true;
        loadError = null;

        try
        {
            var result = await ContributionClient.GetBoxsetChat.ExecuteAsync(BoxsetId!, 100, null);

            if (result.IsSuccessResult())
            {
                chatItems = result.Data?.BoxsetChat?.Nodes?.ToList()
                    ?? new List<IGetBoxsetChat_BoxsetChat_Nodes>();

                await ContributionClient.MarkBoxsetMessagesAsRead.ExecuteAsync(new MarkBoxsetMessagesAsReadInput
                {
                    BoxsetId = BoxsetId!
                });
            }
            else
            {
                loadError = "Failed to load messages.";
            }
        }
        catch (Exception ex)
        {
            loadError = $"Error loading messages: {ex.Message}";
        }
        finally
        {
            isLoading = false;
        }
    }

    private async Task SendMessage()
    {
        if (string.IsNullOrWhiteSpace(newMessage) || string.IsNullOrEmpty(BoxsetId))
            return;

        isSending = true;
        sendError = null;

        try
        {
            var result = await ContributionClient.SendBoxsetUserMessage.ExecuteAsync(new SendBoxsetUserMessageInput
            {
                BoxsetId = BoxsetId,
                Message = newMessage
            });

            if (result.Data?.SendBoxsetUserMessage?.Errors is { Count: > 0 } errors)
            {
                var error = errors[0];
                sendError = error switch
                {
                    ISendBoxsetUserMessage_SendBoxsetUserMessage_Errors_BoxsetNotFoundError e => e.Message,
                    ISendBoxsetUserMessage_SendBoxsetUserMessage_Errors_AuthenticationError e => e.Message,
                    ISendBoxsetUserMessage_SendBoxsetUserMessage_Errors_InvalidIdError e => e.Message,
                    ISendBoxsetUserMessage_SendBoxsetUserMessage_Errors_InvalidOwnershipError e => e.Message,
                    _ => "An unexpected error occurred."
                };
                return;
            }

            newMessage = string.Empty;
            showPreview = false;
            await LoadMessages();
        }
        catch (Exception ex)
        {
            sendError = $"Failed to send message: {ex.Message}";
        }
        finally
        {
            isSending = false;
        }
    }

    private static string GetBadgeClass(UserMessageType type) => type switch
    {
        UserMessageType.AdminMessage => "bg-primary",
        UserMessageType.UserMessage => "bg-secondary",
        _ => "bg-light text-dark"
    };

    private static string GetTypeLabel(UserMessageType type) => type switch
    {
        UserMessageType.AdminMessage => "Admin",
        UserMessageType.UserMessage => "You",
        _ => type.ToString()
    };

    private static readonly MarkdownPipeline MarkdownPipeline = new MarkdownPipelineBuilder()
        .UseAdvancedExtensions()
        .Build();

    private static string RenderMarkdown(string? text)
    {
        if (string.IsNullOrEmpty(text))
            return string.Empty;

        return Markdown.ToHtml(text, MarkdownPipeline);
    }
}
