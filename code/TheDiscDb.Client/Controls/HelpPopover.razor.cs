using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;

namespace TheDiscDb.Client.Controls;

public partial class HelpPopover : IAsyncDisposable
{
    private readonly string popoverId = $"help-popover-{Guid.NewGuid():N}";
    private ElementReference rootElement;
    private DotNetObjectReference<HelpPopover>? selfReference;
    private IJSObjectReference? module;
    private bool isPinned;
    private bool listenersRegistered;
    private bool suppressInteractionOpen;

    [Inject]
    private IJSRuntime JS { get; set; } = default!;

    [Parameter]
    public required RenderFragment ChildContent { get; set; }

    [Parameter]
    public string AccessibleLabel { get; set; } = "More information";

    [Parameter]
    public string? CssClass { get; set; }

    private string RootCssClass
    {
        get
        {
            var classes = new List<string> { "help-popover" };

            if (this.isPinned)
            {
                classes.Add("help-popover--pinned");
            }

            if (this.suppressInteractionOpen)
            {
                classes.Add("help-popover--suppressed");
            }

            if (!string.IsNullOrWhiteSpace(this.CssClass))
            {
                classes.Add(this.CssClass);
            }

            return string.Join(' ', classes);
        }
    }

    private void HandlePointerLeave()
    {
        this.suppressInteractionOpen = false;
    }

    private void HandleFocusOut()
    {
        this.suppressInteractionOpen = false;
    }

    private async Task TogglePinnedAsync()
    {
        this.isPinned = !this.isPinned;
        this.suppressInteractionOpen = !this.isPinned;

        if (this.isPinned)
        {
            await this.RegisterDismissListenersAsync();
        }
        else
        {
            await this.UnregisterDismissListenersAsync();
        }
    }

    [JSInvokable]
    public async Task DismissAsync()
    {
        if (!this.isPinned)
        {
            return;
        }

        this.isPinned = false;
        this.suppressInteractionOpen = true;
        await this.UnregisterDismissListenersAsync();
        await this.InvokeAsync(this.StateHasChanged);
    }

    public async ValueTask DisposeAsync()
    {
        await this.UnregisterDismissListenersAsync();
        this.selfReference?.Dispose();

        if (this.module is not null)
        {
            await this.module.DisposeAsync();
        }
    }

    private async Task RegisterDismissListenersAsync()
    {
        this.module ??= await this.JS.InvokeAsync<IJSObjectReference>(
            "import",
            "/help-popover.js");
        this.selfReference ??= DotNetObjectReference.Create(this);

        await this.module.InvokeVoidAsync(
            "register",
            this.rootElement,
            this.selfReference);
        this.listenersRegistered = true;
    }

    private async Task UnregisterDismissListenersAsync()
    {
        if (!this.listenersRegistered || this.module is null)
        {
            return;
        }

        await this.module.InvokeVoidAsync("unregister", this.rootElement);
        this.listenersRegistered = false;
    }
}
