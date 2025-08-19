namespace TheDiscDb.Search;

public interface ISearchIndexService
{
    Task<BuildIndexSummary> BuildIndex();
    Task<BuildIndexSummary> IndexItems(IEnumerable<SearchEntry> entries, int batchSize = 10);
}

public class NullSearchIndexService : ISearchIndexService
{
    public Task<BuildIndexSummary> BuildIndex()
    {
       var summary = new BuildIndexSummary
       {
           Success = true,
           ItemCount = 0,
           Duration = TimeSpan.Zero
       };

       return Task.FromResult(summary);
    }

    public Task<BuildIndexSummary> IndexItems(IEnumerable<SearchEntry> entries, int batchSize = 10)
    {
        var summary = new BuildIndexSummary
        {
            Success = true,
            ItemCount = 0,
            Duration = TimeSpan.Zero
        };

        return Task.FromResult(summary);
    }
}
