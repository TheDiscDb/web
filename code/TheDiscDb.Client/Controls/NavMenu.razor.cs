using System.Net.Http.Json;
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
    public HttpClient Http { get; set; } = null!;

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

    private string? searchQuery;
    private List<SearchEntry>? suggestions;
    private bool showSuggestions;
    private int selectedIndex = -1;
    private CancellationTokenSource? debounceCts;

    private const int DebounceMs = 500;
    private const int MinQueryLength = 3;
    private const int SuggestionLimit = 5;

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

    public async Task OnSearchSubmit()
    {
        if (string.IsNullOrWhiteSpace(searchQuery)) return;

        showSuggestions = false;
        await DismissMobileMenu();

        string url = $"/search?q={Uri.EscapeDataString(searchQuery)}";
        NavigationManager?.NavigateTo(url);
    }

    private async Task OnSearchInput(ChangeEventArgs e)
    {
        searchQuery = e.Value?.ToString();
        selectedIndex = -1;

        if (string.IsNullOrWhiteSpace(searchQuery) || searchQuery.Length < MinQueryLength)
        {
            showSuggestions = false;
            suggestions = null;
            return;
        }

        debounceCts?.Cancel();
        debounceCts = new CancellationTokenSource();
        var token = debounceCts.Token;

        try
        {
            await Task.Delay(DebounceMs, token);
            if (token.IsCancellationRequested) return;

            var results = await Http.GetFromJsonAsync<List<SearchEntry>>(
                $"/api/search/suggest?q={Uri.EscapeDataString(searchQuery)}&limit={SuggestionLimit}",
                token);

            if (!token.IsCancellationRequested && results != null)
            {
                suggestions = results;
                showSuggestions = suggestions.Any();
                StateHasChanged();
            }
        }
        catch (TaskCanceledException) { }
        catch (Exception) { showSuggestions = false; }
    }

    private async Task OnSearchKeyDown(KeyboardEventArgs e)
    {
        if (!showSuggestions || suggestions == null || !suggestions.Any())
            return;

        switch (e.Key)
        {
            case "ArrowDown":
                selectedIndex = Math.Min(selectedIndex + 1, suggestions.Count - 1);
                break;
            case "ArrowUp":
                selectedIndex = Math.Max(selectedIndex - 1, -1);
                break;
            case "Enter" when selectedIndex >= 0 && selectedIndex < suggestions.Count:
                await NavigateToSuggestion(suggestions[selectedIndex]);
                break;
            case "Escape":
                showSuggestions = false;
                break;
        }
    }

    private void OnSearchFocus()
    {
        if (suggestions != null && suggestions.Any() && !string.IsNullOrWhiteSpace(searchQuery) && searchQuery.Length >= MinQueryLength)
        {
            showSuggestions = true;
        }
    }

    private void OnSearchBlur()
    {
        // Delay to allow click on suggestion to fire before hiding
        _ = Task.Delay(200).ContinueWith(_ =>
        {
            showSuggestions = false;
            InvokeAsync(StateHasChanged);
        });
    }

    private async Task NavigateToSuggestion(SearchEntry suggestion)
    {
        showSuggestions = false;
        await DismissMobileMenu();

        if (!string.IsNullOrEmpty(suggestion.RelativeUrl))
        {
            NavigationManager?.NavigateTo(suggestion.RelativeUrl.ToLower());
        }
    }

    private async Task DismissMobileMenu()
    {
        await JS.InvokeVoidAsync("eval", "document.querySelector('#navbarOffcanvas.show')?.classList.remove('show');document.querySelector('.offcanvas-backdrop')?.remove();document.body.classList.remove('overflow-hidden');");
    }
}