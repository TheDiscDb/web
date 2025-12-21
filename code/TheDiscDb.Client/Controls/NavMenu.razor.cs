using System.Security.Claims;
using System.Web;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Components.Web;
using TheDiscDb.Search;

namespace TheDiscDb.Client.Controls;

public partial class NavMenu : ComponentBase
{
    [Inject]
    public NavigationManager? NavigationManager { get; set; }

    [Inject]
    public ApiClient SearchClient { get; set; } = null!;

    public IEnumerable<SearchEntry>? SearchResults { get; set; }

    [Inject]
    AuthenticationStateProvider? AuthProvider { get; set; }

    string DisplayName { get; set; } = "";
    string ShortName { get; set; } = "";

#pragma warning disable IDE0044 // Add readonly modifier
    private string? searchQuery;
#pragma warning restore IDE0044 // Add readonly modifier

    protected override async Task OnInitializedAsync()
    {
        if (this.AuthProvider != null)
        {
            var state = await this.AuthProvider.GetAuthenticationStateAsync();
            if (state != null)
            {
                this.DisplayName = state.User.FindFirstValue(ClaimTypes.Name) ?? "";
                Console.Write("From Claim: " + DisplayName);
                if (this.DisplayName.Length > 0)
                {
                    this.ShortName = Char.ToUpperInvariant(this.DisplayName[0]).ToString();
                }
            }
            else
            {
                Console.WriteLine("No Authentication state");
            }
        }
        else
        {
            Console.WriteLine("No AuthProvider");
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

        string url = $"/search/{HttpUtility.UrlEncode(searchQuery)}";
        NavigationManager?.NavigateTo(url);
    }
}