using System.Security.Claims;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.JSInterop;
using StrawberryShake;
using Syncfusion.Blazor.SplitButtons;
using TheDiscDb.Client.Contributions;
using TheDiscDb.Search;

namespace TheDiscDb.Client.Controls;

public partial class NavMenu : ComponentBase
{
    [Inject]
    public NavigationManager? NavigationManager { get; set; }

    [Inject]
    public ApiClient SearchClient { get; set; } = null!;

    [Inject]
    public IJSRuntime JS { get; set; } = null!;

    [Inject]
    AuthenticationStateProvider? AuthProvider { get; set; }

    [Inject]
    IContributionClient? ContributionClient { get; set; }

    public IEnumerable<SearchEntry>? SearchResults { get; set; }

    string DisplayName { get; set; } = "";
    string ShortName { get; set; } = "";
    bool isAuthenticated;
    bool hasUnreadMessages;

#pragma warning disable IDE0044 // Add readonly modifier
    private string? searchQuery;
#pragma warning restore IDE0044 // Add readonly modifier

    protected override async Task OnInitializedAsync()
    {
        if (this.AuthProvider != null)
        {
            var state = await this.AuthProvider.GetAuthenticationStateAsync();
            if (state?.User?.Identity?.IsAuthenticated == true)
            {
                isAuthenticated = true;
                this.DisplayName = state.User.FindFirstValue(ClaimTypes.Name) ?? "";
                if (this.DisplayName.Length > 0)
                {
                    this.ShortName = Char.ToUpperInvariant(this.DisplayName[0]).ToString();
                }

                await CheckUnreadMessages();
            }
        }
    }

    private async Task CheckUnreadMessages()
    {
        try
        {
            if (ContributionClient != null)
            {
                var result = await ContributionClient.HasUnreadMessages.ExecuteAsync();
                if (result.IsSuccessResult())
                {
                    hasUnreadMessages = result.Data?.HasUnreadMessages ?? false;
                }
            }
        }
        catch
        {
            // Silently ignore — unread badge is non-critical
        }
    }

    private async Task OnUserMenuItemSelected(MenuEventArgs args)
    {
        switch (args.Item.Id)
        {
            case "my-contributions":
                NavigationManager?.NavigateTo("/contribute/my");
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

    public async Task KeyPressed(KeyboardEventArgs e)
    {
        if (e.Code == "Enter" || e.Code == "NumpadEnter")
        {
            await Task.Yield();
            TryNavigateToSearch();
        }
    }

    private void TryNavigateToSearch()
    {
        if (string.IsNullOrWhiteSpace(searchQuery)) return;

        string url = $"/search?q={Uri.EscapeDataString(searchQuery)}";
        NavigationManager?.NavigateTo(url);
    }
}