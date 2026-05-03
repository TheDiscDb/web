namespace TheDiscDb.Data.Import.Pipeline;

using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using TheDiscDb.Search;

public class SearchIndexUpdateMiddleware : IMiddleware
{
    private readonly ISearchIndexService searchIndexService;
    private readonly ILogger<SearchIndexUpdateMiddleware> logger;

    public SearchIndexUpdateMiddleware(ISearchIndexService searchIndexService)
        : this(searchIndexService, NullLogger<SearchIndexUpdateMiddleware>.Instance)
    {
    }

    public SearchIndexUpdateMiddleware(ISearchIndexService searchIndexService, ILogger<SearchIndexUpdateMiddleware> logger)
    {
        this.searchIndexService = searchIndexService ?? throw new ArgumentNullException(nameof(searchIndexService));
        this.logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public Func<ImportItem, CancellationToken, Task> Next { get; set; } = (_, _) => Task.CompletedTask;

    public async Task Process(ImportItem item, CancellationToken cancellationToken)
    {
        if (item.MediaItem != null)
        {
            var searchEntries = item.MediaItem.ToSearchEntries();
            var summary = await this.searchIndexService.IndexItems(searchEntries);
            this.LogSummary(summary, "MediaItem", item.MediaItem.Slug, item.MediaItem.Title);
        }
        else if (item.Boxset != null)
        {
            var searchEntries = item.Boxset.ToSearchEntries();
            var summary = await this.searchIndexService.IndexItems(searchEntries);
            this.LogSummary(summary, "Boxset", item.Boxset.Slug, item.Boxset.Title);
        }

        await this.Next(item, cancellationToken);
    }

    private void LogSummary(BuildIndexSummary summary, string itemType, string slug, string title)
    {
        if (summary == null)
        {
            return;
        }

        if (!summary.Success)
        {
            this.logger.LogError(
                "Search index update failed for {ItemType} '{Title}' (slug={Slug}): indexed {ItemCount} document(s) in {Duration}; first error: {Error}",
                itemType,
                title,
                slug,
                summary.ItemCount,
                summary.Duration,
                summary.ErrorMessage);
        }
        else
        {
            this.logger.LogDebug(
                "Search index update succeeded for {ItemType} '{Title}' (slug={Slug}): indexed {ItemCount} document(s) in {Duration}.",
                itemType,
                title,
                slug,
                summary.ItemCount,
                summary.Duration);
        }
    }
}
