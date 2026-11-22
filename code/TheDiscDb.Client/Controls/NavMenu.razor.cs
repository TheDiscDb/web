using System.Security.Claims;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Components.Routing;
using Microsoft.JSInterop;
using Syncfusion.Blazor.SplitButtons;
using TheDiscDb.Client.Services;

namespace TheDiscDb.Client.Controls;

public partial class NavMenu : ComponentBase, IAsyncDisposable
{
    [Inject]
    public NavigationManager? NavigationManager { get; set; }

    [Inject]
    public IJSRuntime JS { get; set; } = null!;

    [Inject]
    AuthenticationStateProvider? AuthProvider { get; set; }

    [Inject]
    IUnreadMessagesService? UnreadMessages { get; set; }

    string DisplayName { get; set; } = "";
    string ShortName { get; set; } = "";
    bool isAuthenticated;
    bool hasUnreadMessages;
    private readonly CancellationTokenSource lifetime = new();
    private bool disposed;
    private int refreshVersion;

    protected override async Task OnInitializedAsync()
    {
        if (this.AuthProvider != null)
            this.AuthProvider.AuthenticationStateChanged += this.OnAuthenticationChanged;
        if (this.NavigationManager != null)
            this.NavigationManager.LocationChanged += this.OnLocationChanged;
        await this.Refresh();
    }

    private async Task Refresh(Task<AuthenticationState>? authenticationState = null)
    {
        if (this.disposed || this.AuthProvider == null)
            return;

        var version = ++this.refreshVersion;
        var token = this.lifetime.Token;
        try
        {
            var state = await (authenticationState ?? this.AuthProvider.GetAuthenticationStateAsync());
            if (token.IsCancellationRequested || version != this.refreshVersion)
                return;

            this.isAuthenticated = state.User.Identity?.IsAuthenticated == true;
            this.DisplayName = this.isAuthenticated ? state.User.FindFirstValue(ClaimTypes.Name) ?? "" : "";
            this.ShortName = this.DisplayName.Length > 0 ? char.ToUpperInvariant(this.DisplayName[0]).ToString() : "";
            if (!this.isAuthenticated)
            {
                this.hasUnreadMessages = false;
                return;
            }

            if (this.UnreadMessages == null)
                return;

            var unread = await this.UnreadMessages.HasUnreadMessagesAsync(token);
            if (!token.IsCancellationRequested && version == this.refreshVersion)
                this.hasUnreadMessages = unread;
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
        catch
        {
            // Silently ignore — unread badge is non-critical
        }
    }

    private async void OnLocationChanged(object? sender, LocationChangedEventArgs args) =>
        await this.RefreshFromEvent();

    private async void OnAuthenticationChanged(Task<AuthenticationState> state) =>
        await this.RefreshFromEvent(state);

    private async Task RefreshFromEvent(Task<AuthenticationState>? state = null)
    {
        if (this.disposed)
            return;
        try
        {
            await this.InvokeAsync(async () =>
            {
                await this.Refresh(state);
                if (!this.disposed)
                    this.StateHasChanged();
            });
        }
        catch (Exception ex)
        {
            if (!this.disposed)
                await this.DispatchExceptionAsync(ex);
        }
    }

    public async ValueTask DisposeAsync()
    {
        this.disposed = true;
        this.refreshVersion++;
        if (this.AuthProvider != null)
            this.AuthProvider.AuthenticationStateChanged -= this.OnAuthenticationChanged;
        if (this.NavigationManager != null)
            this.NavigationManager.LocationChanged -= this.OnLocationChanged;
        await this.lifetime.CancelAsync();
        this.lifetime.Dispose();
    }

    private async Task OnUserMenuItemSelected(MenuEventArgs args)
    {
        switch (args.Item.Id)
        {
            case "my-contributions":
                NavigationManager?.NavigateTo("/contribute/my");
                break;
            case "my-changes":
                NavigationManager?.NavigateTo("/changes/my");
                break;
            case "messages":
                NavigationManager?.NavigateTo("/messages");
                break;
            case "logout":
                // Logout endpoint has antiforgery disabled for WebAssembly compatibility
                await JS.InvokeVoidAsync("eval", """
                    var form = document.createElement('form');
                    form.method = 'post';
                    form.action = 'Account/Logout';
                    var returnUrlInput = document.createElement('input');
                    returnUrlInput.type = 'hidden';
                    returnUrlInput.name = 'ReturnUrl';
                    returnUrlInput.value = '';
                    form.appendChild(returnUrlInput);
                    document.body.appendChild(form);
                    form.submit();
                """);
                break;
            case "login":
                NavigationManager?.NavigateTo("/Account/Login", forceLoad: true);
                break;
        }
    }
}