using System.Web;
using Microsoft.AspNetCore.Components;
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

#pragma warning disable IDE0044 // Add readonly modifier
    private string? searchQuery;
#pragma warning restore IDE0044 // Add readonly modifier

    //private async Task OnHandleReadData(AutocompleteReadDataEventArgs autocompleteReadDataEventArgs)
    //{
    //    if (!autocompleteReadDataEventArgs.CancellationToken.IsCancellationRequested)
    //    {
    //        var results = await this.SearchClient.Search(autocompleteReadDataEventArgs.SearchValue);
    //        if (!autocompleteReadDataEventArgs.CancellationToken.IsCancellationRequested)
    //        {
    //            this.SearchResults = results;
    //        }
    //    }
    //}

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