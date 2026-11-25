namespace TheDiscDb.Client.Pages;

using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;
using TheDiscDb.Client.Interop;
using TheDiscDb.Client.Services;

public partial class SearchDisc
{
    [SupplyParameterFromQuery(Name = "dischash")] public string? DiscHash { get; set; }

    [Inject] private DiscDirectoryPicker Picker { get; set; } = default!;
    [Inject] private DiscSearchService Search { get; set; } = default!;
    [Inject] private HttpClient Http { get; set; } = default!;
    [Inject] private DiscScanUploader Uploader { get; set; } = default!;
    [Inject] private ContributionContinuationService Continuation { get; set; } = default!;
    [Inject] private NavigationManager Navigation { get; set; } = default!;
    [Inject] private ILogger<SearchDisc> Logger { get; set; } = default!;

    private readonly CancellationTokenSource lifetime = new();
    private DiscSearchSession? session;
    private DiscSearchContribution? prepared;
    private bool ready;
    private bool busy;
    private bool showCompatibilityPicker;
    private string? error;
    private string? loadedHash;
    private string? pendingHash;
    private bool hasPendingHash;
    private string progress = "Reading disc identifiers...";

    protected override void OnInitialized() => this.session = new DiscSearchSession(this.Search, this.Uploader);

    protected override void OnParametersSet()
    {
        string? hash = string.IsNullOrWhiteSpace(this.DiscHash) ? null : this.DiscHash.Trim();
        if (!string.Equals(hash, this.loadedHash, StringComparison.OrdinalIgnoreCase))
        {
            this.loadedHash = hash;
            this.pendingHash = hash;
            this.hasPendingHash = true;
            this.prepared = null;
            this.error = null;
        }
    }

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (firstRender)
        {
            await this.InitializePickerAsync();
            if (!this.lifetime.IsCancellationRequested)
            {
                this.StateHasChanged();
            }
        }
        while (this.hasPendingHash && !this.busy && !this.lifetime.IsCancellationRequested)
        {
            string? hash = this.pendingHash;
            this.hasPendingHash = false;
            await this.RunAsync(async () =>
            {
                this.prepared = null;
                this.progress = "Checking the database...";
                this.StateHasChanged();
                if (hash is null)
                {
                    await this.session!.ClearAsync();
                }
                else
                {
                    await this.session!.SearchHashAsync(hash, this.lifetime.Token);
                }
            });
            if (!this.lifetime.IsCancellationRequested)
            {
                this.StateHasChanged();
            }
        }
    }

    private Task InitializePickerAsync() => this.RunAsync(async () =>
    {
        await this.Picker.PreloadAsync(this.lifetime.Token);
        this.ready = true;
    });

    private Task SearchAsync(bool compatibility) => this.RunAsync(async () =>
    {
        var selection = await this.Picker.PickAsync(this.lifetime.Token,
            useCompatibilityPicker: compatibility,
            onNativePickerAborted: () => this.showCompatibilityPicker = true);
        if (selection is null)
        {
            return;
        }
        if (this.lifetime.IsCancellationRequested)
        {
            await selection.DisposeAsync();
            return;
        }
        this.prepared = null;
        this.progress = "Reading disc identifiers and checking the database...";
        this.StateHasChanged();
        await this.session!.SearchAsync(selection, this.lifetime.Token);
        await this.SuggestIdentifiersAsync();
    });

    private Task RetryAsync() => this.RunAsync(async () =>
    {
        this.prepared = null;
        this.progress = "Checking the database...";
        await this.session!.RetryAsync(this.lifetime.Token);
        await this.SuggestIdentifiersAsync();
    });

    private async Task SuggestIdentifiersAsync()
    {
        try
        {
            await this.session!.SuggestMissingIdentifiersAsync(this.Http, this.lifetime.Token);
        }
        catch (HttpRequestException ex)
        {
            this.Logger.LogWarning(ex, "Could not submit missing disc identifiers from disc search");
        }
    }

    private Task StartNewSearchAsync() => this.RunAsync(async () =>
    {
        await this.session!.ClearAsync();
        this.prepared = null;
        this.showCompatibilityPicker = false;
        this.Navigation.NavigateTo("/search/disc");
    });

    private Task PrepareAsync(DiscTitleMatch? title) => this.RunAsync(async () =>
    {
        this.prepared = await this.session!.PrepareContributionAsync(
            title, this.Navigation.BaseUri, this.ReportProgress, this.lifetime.Token);
        if (this.prepared.Warnings.Count == 0)
        {
            await this.NavigateToContributionAsync(this.prepared);
        }
    });

    private Task ContinueAsync() => this.RunAsync(() => this.NavigateToContributionAsync(this.prepared!));

    private Task NavigateToContributionAsync(DiscSearchContribution contribution)
    {
        if (contribution.Title is not { } title)
        {
            this.Navigation.NavigateTo(ContributionContinuation.SearchUrl("movie", contribution.Hash));
            return Task.CompletedTask;
        }
        return this.Continuation.ContinueAsync(new ContributionContinuation(
            title.MediaType!, title.ExternalId!, contribution.Hash, FromMatches: true), this.lifetime.Token);
    }

    private void ReportProgress(string message)
    {
        if (!this.lifetime.IsCancellationRequested)
        {
            this.progress = message;
            this.StateHasChanged();
        }
    }

    private async Task RunAsync(Func<Task> action)
    {
        if (this.busy || this.lifetime.IsCancellationRequested)
        {
            return;
        }
        this.busy = true;
        this.error = null;
        try
        {
            await action();
        }
        catch (OperationCanceledException) when (this.lifetime.IsCancellationRequested)
        {
        }
        catch (Exception ex) when (ex is InvalidOperationException or IOException or HttpRequestException
            or JSException or System.Text.Json.JsonException or UnauthorizedAccessException)
        {
            this.Logger.LogWarning(ex, "Disc search or contribution preparation failed");
            this.error = ex is InvalidOperationException ? ex.Message
                : "Could not read the disc or complete the search. Retry, or select the disc folder again. Keep the disc in the drive until scanning finishes.";
        }
        finally
        {
            this.busy = false;
        }
    }

    public async ValueTask DisposeAsync()
    {
        await this.lifetime.CancelAsync();
        if (this.session is not null)
        {
            await this.session.DisposeAsync();
        }
        this.lifetime.Dispose();
    }
}
