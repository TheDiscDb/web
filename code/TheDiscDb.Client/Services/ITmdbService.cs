using FluentResults;

namespace TheDiscDb.Services;

public interface IExternalSearchService
{
    Task<Result<ExternalSearchResponse>> SearchMovies(string query, CancellationToken cancellationToken);
    Task<Result<ExternalSearchResponse>> SearchSeries(string query, CancellationToken cancellationToken);
}

public class ExternalSearchResponse
{
    public List<ExternalSearchResult> Results { get; set; } = new();
}

public class ExternalSearchResult
{
    public int Id { get; set; }
    public string Title { get; set; } = string.Empty;
    public int Year { get; set; }
    public string ImageUrl { get; set; } = string.Empty;
}
