namespace TheDiscDb.Client.Pages.Contribute;

using Microsoft.AspNetCore.Components;
using TheDiscDb.Services;

public partial class TitleSearchInput
{
    [Parameter] public string MediaType { get; set; } = "movie";
    [Parameter] public string? SearchText { get; set; }
    [Parameter] public EventCallback<string?> SearchTextChanged { get; set; }
    [Parameter] public ExternalSearchResult? SelectedItem { get; set; }
    [Parameter] public EventCallback<ExternalSearchResult?> SelectedItemChanged { get; set; }
    [Inject] private IExternalSearchService Search { get; set; } = default!;

    private string? text;
    private string? appliedMediaType;
    private IReadOnlyList<ExternalSearchResult> results = [];
    private string? error;
    private bool isSearching;
    private CancellationTokenSource? searchCancellation;
    private int revision;

    protected override async Task OnParametersSetAsync()
    {
        if (this.appliedMediaType != this.MediaType || this.text != this.SearchText)
        {
            this.appliedMediaType = this.MediaType;
            this.text = this.SearchText;
            await SearchAsync();
        }
    }

    private async Task TextChangedAsync(ChangeEventArgs args)
    {
        this.text = args.Value?.ToString();
        await this.SelectedItemChanged.InvokeAsync(null);
        await this.SearchTextChanged.InvokeAsync(this.text);
        await SearchAsync();
    }

    private async Task SearchAsync()
    {
        this.searchCancellation?.Cancel();
        this.searchCancellation?.Dispose();
        this.searchCancellation = new CancellationTokenSource();
        var token = this.searchCancellation.Token;
        int currentRevision = ++this.revision;
        this.results = [];
        this.error = null;
        this.isSearching = !string.IsNullOrWhiteSpace(this.text);
        if (!this.isSearching)
        {
            return;
        }

        try
        {
            await Task.Delay(250, token);
            var response = this.MediaType == "series"
                ? await this.Search.SearchSeries(this.text!, token)
                : await this.Search.SearchMovies(this.text!, token);
            if (currentRevision != this.revision)
            {
                return;
            }
            this.results = response.IsSuccess ? response.Value.Results : [];
            this.error = this.results.Count == 0 ? "No titles found. Try another search or enter a TMDB ID." : null;
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
        }
        catch (HttpRequestException)
        {
            if (currentRevision == this.revision)
            {
                this.error = "Search is unavailable. Try again or enter a TMDB ID.";
            }
        }
        finally
        {
            if (currentRevision == this.revision)
            {
                this.isSearching = false;
            }
        }
    }

    public async ValueTask DisposeAsync()
    {
        ++this.revision;
        if (this.searchCancellation is not null)
        {
            await this.searchCancellation.CancelAsync();
            this.searchCancellation.Dispose();
        }
    }
}