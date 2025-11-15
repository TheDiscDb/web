using Syncfusion.Blazor;
using TheDiscDb.Services;

namespace TheDiscDb.Client;

public class ExternalSearchDataAdaptor : DataAdaptor
{
    private readonly IExternalSearchService client;
    private CancellationTokenSource cancellationTokenSource;

    public string? MediaType { get; set; }

    public ExternalSearchDataAdaptor(IExternalSearchService client)
    {
        this.client = client ?? throw new ArgumentNullException(nameof(client));
        this.cancellationTokenSource = new CancellationTokenSource();
    }

    public override async Task<object> ReadAsync(DataManagerRequest dataManagerRequest, string? additionalParam = null)
    {
        var currentCancellationTokenSource = new CancellationTokenSource();
        var previousCancellationTokenSource = Interlocked.Exchange(ref this.cancellationTokenSource, currentCancellationTokenSource);
        previousCancellationTokenSource.Cancel();

        if (dataManagerRequest.Where == null || dataManagerRequest.Where.Count == 0)
        {
            return Enumerable.Empty<ExternalSearchResult>();
        }

        var firstWhere = dataManagerRequest.Where.FirstOrDefault();
        string term = firstWhere?.value?.ToString() ?? string.Empty;

        if (string.IsNullOrWhiteSpace(term))
        {
            return Enumerable.Empty<ExternalSearchResult>();
        }

        if (MediaType!.Equals("series", StringComparison.OrdinalIgnoreCase))
        {
            var seriesResults = await client.SearchSeries(term, currentCancellationTokenSource.Token);
            if (seriesResults != null)
            {
                return seriesResults.Value.Results;
            }
        }
        else
        {
            var movieResults = await client.SearchMovies(term, currentCancellationTokenSource.Token);
            if (movieResults != null)
            {
                return movieResults.Value.Results;
            }
        }

        return Enumerable.Empty<ExternalSearchResult>();
    }
}
