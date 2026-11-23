using System.Security.Claims;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;
using TheDiscDb.Client.Contributions;
using TheDiscDb.Client.Controls;

namespace TheDiscDb.Client.Pages.Contribute;

public abstract class MessageConversationBase : CancellableComponentBase
{
    [Inject]
    protected IContributionClient ContributionClient { get; set; } = null!;

    [Inject]
    private AuthenticationStateProvider AuthProvider { get; set; } = null!;

    protected List<ConversationMessage> chatItems = [];
    protected bool isLoading = true;
    protected string? loadError;
    protected string newMessage = string.Empty;
    protected string? sendError;
    protected bool isSending;
    protected bool showPreview;
    protected bool isLoadingOlder;
    protected bool hasOlder;
    protected string? readError;
    protected string? viewerId;
    protected bool isAdmin;
    protected (string Text, string Url)[] breadcrumbItems = [];

    private string? loadedThreadId;
    private string? endCursor;
    private CancellationTokenSource? threadCancellation;
    private bool acknowledgementPending;
    private bool acknowledging;
    private bool disposed;

    protected abstract string? ThreadId { get; }
    protected abstract (string Text, string Url)[] Breadcrumbs { get; }
    protected abstract Task<MessagePage> GetPage(string threadId, string? after, CancellationToken cancellationToken);
    protected abstract Task<string?> Send(string threadId, string message, CancellationToken cancellationToken);
    protected abstract Task<bool> Acknowledge(string threadId, int[] messageIds, CancellationToken cancellationToken);

    protected record MessagePage(List<ConversationMessage> Messages, bool HasOlder, string? EndCursor, string? ViewerId);

    protected override async Task OnParametersSetAsync()
    {
        if (this.loadedThreadId == this.ThreadId && this.threadCancellation != null)
            return;

        this.threadCancellation?.Cancel();
        this.threadCancellation?.Dispose();
        this.threadCancellation = CancellationTokenSource.CreateLinkedTokenSource(this.CancellationToken);
        this.loadedThreadId = this.ThreadId;
        this.chatItems = [];
        this.newMessage = string.Empty;
        this.isSending = false;
        this.showPreview = false;
        this.readError = null;
        this.sendError = null;
        this.acknowledgementPending = false;
        this.breadcrumbItems = this.Breadcrumbs;

        var token = this.threadCancellation.Token;
        var state = await this.AuthProvider.GetAuthenticationStateAsync();
        if (token.IsCancellationRequested)
            return;
        this.viewerId = state.User.FindFirstValue(ClaimTypes.NameIdentifier);
        this.isAdmin = state.User.IsInRole("Administrator");
        if (string.IsNullOrEmpty(this.ThreadId))
        {
            this.loadError = "Invalid conversation ID.";
            this.isLoading = false;
            return;
        }

        await this.LoadMessages(false);
    }

    protected async Task LoadOlder() => await this.LoadMessages(true);

    private async Task LoadMessages(bool older)
    {
        if (older && (!this.hasOlder || this.isLoadingOlder))
            return;

        var threadId = this.ThreadId!;
        var token = this.threadCancellation!.Token;
        this.isLoading = !older;
        this.isLoadingOlder = older;
        this.loadError = null;
        try
        {
            var page = await this.GetPage(threadId, older ? this.endCursor : null, token);
            token.ThrowIfCancellationRequested();
            if (string.IsNullOrEmpty(page.ViewerId))
                throw new InvalidOperationException("Unable to identify the authenticated viewer.");
            // WASM authentication snapshots need not include the identity ID.
            this.viewerId = page.ViewerId;
            this.chatItems = older
                ? this.chatItems.Concat(page.Messages).DistinctBy(m => m.Id).ToList()
                : page.Messages;
            this.hasOlder = page.HasOlder;
            this.endCursor = page.EndCursor;
            this.acknowledgementPending = true;
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
        catch (Exception)
        {
            this.loadError = "Failed to load messages. Please try again.";
        }
        finally
        {
            if (!token.IsCancellationRequested)
            {
                this.isLoading = false;
                this.isLoadingOlder = false;
            }
        }
    }

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (!this.acknowledgementPending || this.acknowledging || this.isLoading)
            return;

        this.acknowledgementPending = false;
        var ids = this.chatItems.Where(m => !m.IsRead &&
            ((this.isAdmin && m.Type == UserMessageType.UserMessage) ||
             (m.Type == UserMessageType.AdminMessage && m.ToUserId == this.viewerId)))
            .Select(m => m.Id).ToArray();
        if (ids.Length == 0)
            return;

        var threadId = this.ThreadId!;
        var token = this.threadCancellation!.Token;
        this.acknowledging = true;
        try
        {
            if (!await this.Acknowledge(threadId, ids, token))
                throw new InvalidOperationException("Read acknowledgement failed.");
            token.ThrowIfCancellationRequested();
            this.chatItems = this.chatItems.Select(m => ids.Contains(m.Id) ? m with { IsRead = true } : m).ToList();
            this.readError = null;
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
        catch (Exception)
        {
            this.readError = "Messages could not be marked read. Your conversation is still available.";
        }
        finally
        {
            this.acknowledging = false;
            if (!this.disposed)
                await this.InvokeAsync(this.StateHasChanged);
        }
    }

    protected void RetryAcknowledgement()
    {
        this.readError = null;
        this.acknowledgementPending = true;
    }

    protected async Task SendMessage()
    {
        if (string.IsNullOrWhiteSpace(this.newMessage) || string.IsNullOrEmpty(this.ThreadId) || this.isSending)
            return;

        var token = this.threadCancellation!.Token;
        this.isSending = true;
        this.sendError = null;
        try
        {
            this.sendError = await this.Send(this.ThreadId, this.newMessage, token);
            token.ThrowIfCancellationRequested();
            if (this.sendError != null)
                return;

            this.newMessage = string.Empty;
            this.showPreview = false;
            await this.LoadMessages(false);
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
        catch (Exception)
        {
            this.sendError = "Failed to send message. Please try again.";
        }
        finally
        {
            if (!token.IsCancellationRequested)
                this.isSending = false;
        }
    }

    protected static string RenderMarkdown(string? text) => MessageTimeline.RenderMarkdown(text);

    public override void Dispose()
    {
        this.disposed = true;
        this.threadCancellation?.Cancel();
        this.threadCancellation?.Dispose();
        base.Dispose();
    }
}