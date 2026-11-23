using Markdig;
using Microsoft.AspNetCore.Components;
using TheDiscDb.Client.Contributions;

namespace TheDiscDb.Client.Controls;

public record ConversationMessage(int Id, string FromUserId, string ToUserId, string Message, bool IsRead, DateTimeOffset CreatedAt, UserMessageType Type);

public partial class MessageTimeline
{
    [Parameter, EditorRequired]
    public IReadOnlyList<ConversationMessage> Messages { get; set; } = [];

    [Parameter]
    public string? ViewerId { get; set; }

    private static readonly MarkdownPipeline MarkdownPipeline = new MarkdownPipelineBuilder()
        .UseAdvancedExtensions()
        .Build();

    public static string GetSenderLabel(ConversationMessage message, string? viewerId) =>
        !string.IsNullOrEmpty(viewerId) && message.FromUserId == viewerId
            ? "You"
            : message.Type == UserMessageType.AdminMessage ? "Admin" : "Contributor";

    public static string RenderMarkdown(string? text) =>
        string.IsNullOrEmpty(text) ? string.Empty : Markdown.ToHtml(text, MarkdownPipeline);
}