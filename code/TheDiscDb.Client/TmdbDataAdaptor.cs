using Fantastic.TheMovieDb;
using Syncfusion.Blazor;
using TheDiscDb.InputModels;

namespace TheDiscDb.Client;

public class TmdbDataAdaptor : DataAdaptor
{
    private readonly TheMovieDbClient client;

    public string? MediaType { get; set; }

    public TmdbDataAdaptor(TheMovieDbClient client)
    {
        this.client = client ?? throw new ArgumentNullException(nameof(client));
    }

    public override async Task<object> ReadAsync(DataManagerRequest dataManagerRequest, string? additionalParam = null)
    {
        var results = new List<MediaItem>();

        if (dataManagerRequest.Where == null || dataManagerRequest.Where.Count == 0)
        {
            return results;
        }

        var firstWhere = dataManagerRequest.Where.FirstOrDefault();
        string term = firstWhere?.value?.ToString() ?? string.Empty;

        if (string.IsNullOrWhiteSpace(term))
        {
            return results;
        }

        if (MediaType!.Equals("series", StringComparison.OrdinalIgnoreCase))
        {
            var seriesResults = await client.SearchTvShowAsync(term, language: "en");
            if (seriesResults != null)
            {
                foreach (var seriesResult in seriesResults.Results)
                {
                    results.Add(new MediaItem
                    {
                        Id = seriesResult.Id,
                        Title = seriesResult.Name,
                        Year = seriesResult.FirstAirDate?.Year ?? 0,
                        ImageUrl = $"https://image.tmdb.org/t/p/w92{seriesResult.PosterPath}"
                    });
                }
            }
        }
        else
        {
            var movieResults = await client.SearchMovieAsync(term, language: "en");
            if (movieResults != null)
            {
                foreach (var movieResult in movieResults.Results)
                {
                    results.Add(new MediaItem
                    {
                        Id = movieResult.Id,
                        Title = movieResult.Title,
                        Year = movieResult.ReleaseDate?.Year ?? 0,
                        ImageUrl = $"https://image.tmdb.org/t/p/w92{movieResult.PosterPath}"
                    });
                }
            }
        }

        return results;
    }
}
