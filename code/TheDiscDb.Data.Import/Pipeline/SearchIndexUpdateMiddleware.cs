namespace TheDiscDb.Data.Import.Pipeline;

using System;
using System.Threading;
using System.Threading.Tasks;
using TheDiscDb.Search;

public class SearchIndexUpdateMiddleware : IMiddleware
{
    private readonly ISearchIndexService searchIndexService;

    public SearchIndexUpdateMiddleware(ISearchIndexService searchIndexService)
    {
        this.searchIndexService = searchIndexService ?? throw new ArgumentNullException(nameof(searchIndexService));
    }

    public Func<ImportItem, CancellationToken, Task> Next { get; set; } = (_, _) => Task.CompletedTask;

    public async Task Process(ImportItem item, CancellationToken cancellationToken)
    {
        if (item.MediaItem != null)
        {
            var searchEntries = item.MediaItem.ToSearchEntries();
            await this.searchIndexService.IndexItems(searchEntries);
        }
        else if (item.Boxset != null)
        {
            var searchEntries = item.Boxset.ToSearchEntries();
            await this.searchIndexService.IndexItems(searchEntries);
        }

        await this.Next(item, cancellationToken);
    }
}
