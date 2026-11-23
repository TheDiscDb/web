using Microsoft.AspNetCore.Components;
using TheDiscDb.Client.Services;
using TheDiscDb.Services;

namespace TheDiscDb.Client.Pages.Contribute;

public partial class IdentifyTitle : CancellableComponentBase
{
    [Inject] private DiscScanContextLoader Loader { get; set; } = default!;
    [Inject] private ContributionContinuationService Continuation { get; set; } = default!;
    [Inject] public NavigationManager NavigationManager { get; set; } = default!;
    [Parameter] public string? MediaType { get; set; }
    [SupplyParameterFromQuery(Name = "boxsetId")] public string? BoxsetId { get; set; }
    [SupplyParameterFromQuery(Name = "discHash")] public string? DiscHash { get; set; }
    [SupplyParameterFromQuery(Name = "q")] public string? Query { get; set; }

    private string mediaType = "movie";
    private string? searchText;
    private ExternalSearchResult? selectedItem;
    private string? manualTmdbId;
    private string? error;
    private bool needsLoad;
    private bool isLoading;
    private bool isContinuing;
    private string? loadedHash;
    private string? loadedBoxset;
    private bool IsNextButtonDisabled => this.isLoading || this.isContinuing || this.error is not null
        || (this.selectedItem is null && (!ulong.TryParse(this.manualTmdbId, out ulong id) || id == 0));
    private string BreadcrumbText => $"Find {(this.mediaType == "series" ? "series" : "movie")}";
    private string SignInUrl => ContributionContinuation.LoginUrl(new Uri(this.NavigationManager.Uri).PathAndQuery);

    protected override void OnParametersSet()
    {
        string nextType = string.Equals(this.MediaType, "series", StringComparison.OrdinalIgnoreCase) ? "series" : "movie";
        if (nextType != this.mediaType || this.loadedHash != this.DiscHash)
        {
            this.selectedItem = null;
            this.manualTmdbId = null;
        }
        this.mediaType = nextType;
        this.searchText = this.Query;
        if (this.loadedHash != this.DiscHash || this.loadedBoxset != this.BoxsetId)
        {
            this.loadedHash = this.DiscHash;
            this.loadedBoxset = this.BoxsetId;
            this.needsLoad = true;
            this.isLoading = !string.IsNullOrWhiteSpace(this.DiscHash);
        }
    }

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (this.needsLoad)
        {
            this.needsLoad = false;
            await LoadScanAsync();
        }
    }

    private async Task LoadScanAsync()
    {
        string? hash = this.DiscHash;
        var cancellationToken = this.CancellationToken;
        this.error = null;
        this.isLoading = !string.IsNullOrWhiteSpace(hash);
        try
        {
            if (!string.IsNullOrWhiteSpace(hash))
            {
                var context = await this.Loader.LoadAsync(hash, cancellationToken);
                if (hash == this.DiscHash && context.Titles.Count > 0
                    && (context.Draft.IsExistingDiscReference || context.Titles.Any(title => title.CanContribute)))
                {
                    this.NavigationManager.NavigateTo(ContributionContinuation.MatchesUrl(hash, this.BoxsetId), replace: true);
                }
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception ex) when (ex is HttpRequestException or InvalidOperationException or Microsoft.JSInterop.JSException or System.Text.Json.JsonException)
        {
            if (hash == this.DiscHash)
            {
                this.error = ex is InvalidOperationException ? ex.Message : "Could not load the saved scan. Retry or scan the disc again.";
            }
        }
        finally
        {
            if (hash == this.DiscHash)
            {
                this.isLoading = false;
            }
        }
        StateHasChanged();
    }

    private void SwitchMediaType(string type)
    {
        var url = ContributionContinuation.SearchUrl(type, this.DiscHash, this.BoxsetId);
        if (!string.IsNullOrEmpty(this.searchText))
        {
            url = Microsoft.AspNetCore.WebUtilities.QueryHelpers.AddQueryString(url, "q", this.searchText);
        }
        this.NavigationManager.NavigateTo(url);
    }

    private async Task NextClicked()
    {
        if (this.IsNextButtonDisabled)
        {
            return;
        }
        this.isContinuing = true;
        try
        {
            await this.Continuation.ContinueAsync(new ContributionContinuation(
                this.mediaType, this.selectedItem?.Id.ToString() ?? this.manualTmdbId!, this.DiscHash, this.BoxsetId), this.CancellationToken);
        }
        catch (Exception ex) when (ex is Microsoft.JSInterop.JSException or System.Text.Json.JsonException or InvalidOperationException)
        {
            this.error = "Your selection could not be saved in this browser. Retry before continuing to login.";
        }
        finally
        {
            this.isContinuing = false;
        }
    }

    private string GetBackUrl() => string.IsNullOrWhiteSpace(this.DiscHash) ? "/contribute" : "/contribute/scan";
}