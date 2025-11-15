using Fantastic.TheMovieDb;
using Fantastic.TheMovieDb.Models;

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
        SearchContainer<SearchMovie>? movieResults = null;

        try
        {
            movieResults = await client.SearchMovieAsync(query, language: "en");
        }
        catch (ResponseJsonException jex)
        {
            return FluentResults.Result.Fail<ExternalSearchResponse>($"TMDb JSON Response Error: {jex.Message}");
        }
        catch (Exception ex)
        {
            return FluentResults.Result.Fail<ExternalSearchResponse>($"TMDb Response Error: {ex.Message}");
        }

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
        SearchContainer<SearchTv>? seriesResults = null;

        try
        {
            seriesResults = await client.SearchTvShowAsync(query, language: "en");
        }
        catch (ResponseJsonException jex)
        {
            return FluentResults.Result.Fail<ExternalSearchResponse>($"TMDb JSON Response Error: {jex.Message}");
        }
        catch (Exception ex)
        {
            return FluentResults.Result.Fail<ExternalSearchResponse>($"TMDb Response Error: {ex.Message}");
        }

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
