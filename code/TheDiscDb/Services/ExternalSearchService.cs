using Fantastic.TheMovieDb;

namespace TheDiscDb.Services.Server;

public class ExternalSearchService : IExternalSearchService
{
    private readonly TheMovieDbClient client;

    public ExternalSearchService(TheMovieDbClient client)
    {
        this.client = client ?? throw new ArgumentNullException(nameof(client));
    }

    public async Task<FluentResults.Result<ExternalSearchResponse>> SearchMovies(string query, CancellationToken cancellationToken)
    {
        var results = new List<ExternalSearchResult>();

        var movieResults = await client.SearchMovieAsync(query, language: "en");
        if (movieResults != null)
        {
            foreach (var movieResult in movieResults.Results)
            {
                results.Add(new ExternalSearchResult
                {
                    Id = movieResult.Id,
                    Title = movieResult.Title!,
                    Year = movieResult.ReleaseDate?.Year ?? 0,
                    ImageUrl = $"https://image.tmdb.org/t/p/w92{movieResult.PosterPath}"
                });
            }
        }

        return new ExternalSearchResponse
        {
            Results = results
        };
    }

    public async Task<FluentResults.Result<ExternalSearchResponse>> SearchSeries(string query, CancellationToken cancellationToken)
    {
        var results = new List<ExternalSearchResult>();

        var seriesResults = await client.SearchTvShowAsync(query, language: "en");
        if (seriesResults != null)
        {
            foreach (var seriesResult in seriesResults.Results)
            {
                results.Add(new ExternalSearchResult
                {
                    Id = seriesResult.Id,
                    Title = seriesResult.Name!,
                    Year = seriesResult.FirstAirDate?.Year ?? 0,
                    ImageUrl = $"https://image.tmdb.org/t/p/w92{seriesResult.PosterPath}"
                });
            }
        }

        return new ExternalSearchResponse
        {
            Results = results
        };
    }
}
