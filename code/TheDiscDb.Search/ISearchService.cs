namespace TheDiscDb.Search;

public interface ISearchService
{
    Task<IEnumerable<SearchEntry>> Search(string term, CancellationToken cancellationToken = default);
    Task<IEnumerable<SearchEntry>> Suggest(string term, int limit = 5, CancellationToken cancellationToken = default);
}

public class NullSearchService : ISearchService
{
    public Task<IEnumerable<SearchEntry>> Search(string term, CancellationToken cancellationToken = default)
    {
        return Task.FromResult(Enumerable.Empty<SearchEntry>());
    }

    public Task<IEnumerable<SearchEntry>> Suggest(string term, int limit = 5, CancellationToken cancellationToken = default)
    {
        return Task.FromResult(Enumerable.Empty<SearchEntry>());
    }
}
