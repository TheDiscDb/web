namespace TheDiscDb.Client.Pages.Contribute;

using Microsoft.AspNetCore.Components;
using TheDiscDb.Client.Services;
using TheDiscDb.Core.DiscHash;

public partial class KnownDiscReleases : CancellableComponentBase
{
    [Parameter] public string? DiscHash { get; set; }
    [SupplyParameterFromQuery(Name = "boxsetId")] public string? BoxsetId { get; set; }
    [Inject] private DiscScanContextLoader Loader { get; set; } = default!;
    [Inject] private ContributionContinuationService Continuation { get; set; } = default!;
    [Inject] private NavigationManager Navigation { get; set; } = default!;

    private DiscScanContext? context;
    private string? error;
    private string hash = "";
    private bool needsLoad;
    private bool isContinuing;
    private string SignInUrl => ContributionContinuation.LoginUrl(new Uri(this.Navigation.Uri).PathAndQuery);

    protected override void OnParametersSet() => this.needsLoad = true;

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (this.needsLoad)
        {
            this.needsLoad = false;
            await LoadAsync();
        }
    }

    private async Task LoadAsync()
    {
        this.error = null;
        this.context = null;
        string? routeHash = this.DiscHash;
        var cancellationToken = this.CancellationToken;
        try
        {
            if (string.IsNullOrWhiteSpace(routeHash) || !DiscScanDraft.TryDecodeHashFromRoute(routeHash, out string decoded))
            {
                throw new InvalidOperationException("The disc hash URL is invalid. Scan the disc again to continue.");
            }
            this.hash = decoded;
            var loaded = await this.Loader.LoadAsync(decoded, cancellationToken);
            if (routeHash != this.DiscHash)
            {
                return;
            }
            this.context = loaded;
            if (loaded.Titles.Count == 0)
            {
                this.Navigation.NavigateTo(ContributionContinuation.SearchUrl("movie", decoded, this.BoxsetId), replace: true);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception ex) when (ex is InvalidOperationException or HttpRequestException or Microsoft.JSInterop.JSException or System.Text.Json.JsonException)
        {
            if (routeHash == this.DiscHash)
            {
                this.error = ex is InvalidOperationException ? ex.Message : "Could not load the saved disc scan. Retry or scan the disc again.";
            }
        }
        StateHasChanged();
    }

    private async Task AddReleaseAsync(DiscTitleMatch title)
    {
        this.isContinuing = true;
        try
        {
            await this.Continuation.ContinueAsync(new ContributionContinuation(
                title.MediaType!, title.ExternalId!, this.hash, this.BoxsetId, FromMatches: true), this.CancellationToken);
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
}