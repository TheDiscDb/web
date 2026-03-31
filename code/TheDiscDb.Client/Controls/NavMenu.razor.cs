using System.Security.Claims;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.JSInterop;
using StrawberryShake;
using Syncfusion.Blazor.SplitButtons;
using TheDiscDb.Client.Contributions;

namespace TheDiscDb.Client.Controls;

public partial class NavMenu : ComponentBase
{
    [Inject]
    public NavigationManager? NavigationManager { get; set; }

    [Inject]
    public IJSRuntime JS { get; set; } = null!;

    [Inject]
    AuthenticationStateProvider? AuthProvider { get; set; }

    [Inject]
    IContributionClient? ContributionClient { get; set; }

    string DisplayName { get; set; } = "";
    string ShortName { get; set; } = "";
    bool isAuthenticated;
    bool hasUnreadMessages;

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
}