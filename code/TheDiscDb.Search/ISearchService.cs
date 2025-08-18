namespace TheDiscDb.Search;

public interface ISearchService
{
    Task<IEnumerable<SearchEntry>> Search(string term, CancellationToken cancellationToken = default);
}