namespace TheDiscDb.Search;

public interface ISearchIndexService
{
    Task<BuildIndexSummary> BuildIndex();
    Task<BuildIndexSummary> IndexItems(IEnumerable<SearchEntry> entries, int batchSize = 10);
    Task<BuildIndexSummary> MergeItems(IEnumerable<SearchDocumentUpdate> updates, int batchSize = 10);
    Task DeleteItems(IEnumerable<string> keys);
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

    public Task DeleteItems(IEnumerable<string> keys)
    {
        return Task.CompletedTask;
    }

    public Task<BuildIndexSummary> MergeItems(IEnumerable<SearchDocumentUpdate> updates, int batchSize = 10)
        => Task.FromResult(new BuildIndexSummary { Success = true, ItemCount = updates.Count() });
}

public sealed record SearchDocumentUpdate(string Key, IDictionary<string, object> Fields);
