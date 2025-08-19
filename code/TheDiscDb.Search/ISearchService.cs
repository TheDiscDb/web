namespace TheDiscDb.Search;

public interface ISearchService
{
    Task<IEnumerable<SearchEntry>> Search(string term, CancellationToken cancellationToken = default);
}

public class NullSearchService : ISearchService
{
    public Task<IEnumerable<SearchEntry>> Search(string term, CancellationToken cancellationToken = default)
    {
        return Task.FromResult(Enumerable.Empty<SearchEntry>());
    }
}
