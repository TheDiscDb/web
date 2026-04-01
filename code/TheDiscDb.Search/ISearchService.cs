namespace TheDiscDb.Search;

public interface ISearchService
{
    Task<IEnumerable<SearchEntry>> Search(string term, int? limit = null, CancellationToken cancellationToken = default);
}

public class NullSearchService : ISearchService
{
    public Task<IEnumerable<SearchEntry>> Search(string term, int? limit = null, CancellationToken cancellationToken = default)
    {
        return Task.FromResult(Enumerable.Empty<SearchEntry>());
    }
}
