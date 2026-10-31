using System.Diagnostics;
using Azure.Search.Documents.Indexes;
using Azure.Search.Documents;
using Azure.Search.Documents.Indexes.Models;
using Azure.Search.Documents.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using TheDiscDb.InputModels;
using TheDiscDb.Web.Data;

namespace TheDiscDb.Search;

public class SearchIndexService : ISearchIndexService
{
    private readonly SearchIndexClient client;
    private readonly IDbContextFactory<SqlServerDataContext> dbFactory;
    private readonly ILogger<SearchIndexService> logger;
    private static readonly string IndexName = "all-items";

    public SearchIndexService(SearchIndexClient client, IDbContextFactory<SqlServerDataContext> dbFactory)
        : this(client, dbFactory, NullLogger<SearchIndexService>.Instance)
    {
    }

    public SearchIndexService(SearchIndexClient client, IDbContextFactory<SqlServerDataContext> dbFactory, ILogger<SearchIndexService> logger)
    {
        this.client = client ?? throw new ArgumentNullException(nameof(client));
        this.dbFactory = dbFactory ?? throw new ArgumentNullException(nameof(dbFactory));
        this.logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public Task<BuildIndexSummary> IndexItems(IEnumerable<SearchEntry> entries, int batchSize = 10)
        => this.IndexActions(entries.Select(entry => IndexDocumentsAction.MergeOrUpload(entry)), batchSize);

    public Task<BuildIndexSummary> MergeItems(IEnumerable<SearchDocumentUpdate> updates, int batchSize = 10)
        => this.IndexActions(updates.Select(update =>
        {
            var document = new SearchDocument(update.Fields) { ["id"] = update.Key };
            return IndexDocumentsAction.Merge(document);
        }), batchSize);

    private async Task<BuildIndexSummary> IndexActions<T>(IEnumerable<IndexDocumentsAction<T>> actions, int batchSize)
    {
        var stopwatch = Stopwatch.StartNew();

        var searchClient = this.client.GetSearchClient(IndexName);
        var result = new BuildIndexSummary
        {
            Success = true
        };

        foreach (var batch in actions.Batch(batchSize))
        {
            try
            {
                var searchEntryBatch = IndexDocumentsBatch.Create(batch.ToArray());
                var itemResult = await searchClient.IndexDocumentsAsync(searchEntryBatch);

                if (!itemResult.Value.Results.All(r => r.Succeeded))
                {
                    result.Success = false;

                    foreach (var failure in itemResult.Value.Results.Where(r => !r.Succeeded))
                    {
                        this.logger.LogWarning(
                            "Search index document failed: key={Key} status={Status} error={Error}",
                            failure.Key,
                            failure.Status,
                            failure.ErrorMessage);
                    }

                    // Surface the first error message in the summary for callers/UI.
                    if (string.IsNullOrEmpty(result.ErrorMessage))
                    {
                        var errorItem = itemResult.Value.Results.FirstOrDefault(r => !r.Succeeded);
                        if (errorItem != null)
                        {
                            result.ErrorMessage = $"Search document '{errorItem.Key}' failed (HTTP {errorItem.Status}): " +
                                (errorItem.ErrorMessage ?? "No error details were returned.");
                        }
                    }
                }

                result.ItemCount += itemResult.Value.Results.Count;
            }
            catch (Exception e)
            {
                result.Success = false;
                result.ErrorMessage ??= e.Message;
                this.logger.LogError(e, "Failed to push a batch of {BatchSize} documents to the search index '{IndexName}'.", batchSize, IndexName);
            }
        }

        stopwatch.Stop();
        result.Duration = stopwatch.Elapsed;

        return result;
    }

    public async Task<BuildIndexSummary> BuildIndex()
    {
        BuildIndexSummary summary = new() { Success = true };
        var stopwatch = Stopwatch.StartNew();

        var index = new SearchIndex(IndexName, this.GetIndexFields());
        await this.client.DeleteIndexAsync(IndexName, CancellationToken.None);
        await this.client.CreateOrUpdateIndexAsync(index);

        using (var dbContext = this.dbFactory.CreateDbContext())
        {
            var actions = this.BuildMediaItemIndex(dbContext);
            var mediaItemresult = await this.IndexItems(actions, RebuildBatchSize);
            summary.ItemCount += mediaItemresult.ItemCount;
            if (!mediaItemresult.Success)
            {
                summary.Success = false;

                if (string.IsNullOrEmpty(summary.ErrorMessage))
                {
                    summary.ErrorMessage = mediaItemresult.ErrorMessage;
                }
            }

            var boxsetActions = this.BuildBoxsetIndex(dbContext);
            var boxsetResult = await this.IndexItems(boxsetActions, RebuildBatchSize);
            summary.ItemCount += boxsetResult.ItemCount;
            if (!boxsetResult.Success)
            {
                summary.Success = false;

                if (string.IsNullOrEmpty(summary.ErrorMessage))
                {
                    summary.ErrorMessage = boxsetResult.ErrorMessage;
                }
            }
        }

        stopwatch.Stop();
        summary.Duration = stopwatch.Elapsed;

        return summary;
    }

    public async Task DeleteItems(IEnumerable<string> keys)
    {
        var keyList = keys.ToList();
        if (keyList.Count == 0) return;

        var searchClient = this.client.GetSearchClient(IndexName);
        var actions = keyList.Select(k => IndexDocumentsAction.Delete("id", k));
        var batch = IndexDocumentsBatch.Create(actions.ToArray());
        await searchClient.IndexDocumentsAsync(batch);
    }

    private IEnumerable<SearchField> GetIndexFields()
    {
        yield return new SimpleField("id", SearchFieldDataType.String)
        {
            IsKey = true,
            IsFacetable = true
        };

        yield return new SimpleField("Type", SearchFieldDataType.String)
        {
            IsFacetable = true,
            IsFilterable = true
        };

        yield return new SearchableField("Title")
        {
            IsFacetable = true,
            IsSortable = true,
            SearchAnalyzerName = LexicalAnalyzerName.StandardLucene,
            IndexAnalyzerName = LexicalAnalyzerName.StandardLucene
        };

        yield return new SimpleField("ImageUrl", SearchFieldDataType.String)
        {
            IsFacetable = true
        };

        yield return new SimpleField("RelativeUrl", SearchFieldDataType.String)
        {
            IsFacetable = true
        };

        yield return new SearchableField("Identifiers", collection: true)
        {
            AnalyzerName = LexicalAnalyzerName.Keyword
        };

        yield return new SearchableField("Groups", collection: true)
        {
            SearchAnalyzerName = LexicalAnalyzerName.StandardLucene,
            IndexAnalyzerName = LexicalAnalyzerName.StandardLucene
        };

        yield return this.GetItemInfoField("MediaItem");
        yield return this.GetItemInfoField("Release");
        yield return this.GetItemInfoField("Disc");
    }

    private SearchField GetItemInfoField(string name)
    {
        var field = new ComplexField(name, collection: false);

        field.Fields.Add(new SimpleField("Id", SearchFieldDataType.Int32)
        {
            IsFacetable = true
        });

        field.Fields.Add(new SimpleField("Slug", SearchFieldDataType.String)
        {
            IsFacetable = true
        });

        field.Fields.Add(new SimpleField("ImageUrl", SearchFieldDataType.String)
        {
            IsFacetable = true
        });

        return field;
    }

    private const int RebuildBatchSize = 1000;
    private const int RebuildPageSize = 100;

    private IEnumerable<SearchEntry> BuildBoxsetIndex(SqlServerDataContext dbContext)
    {
        int lastId = 0;
        while (true)
        {
            var page = dbContext.BoxSets
                .Where(item => item.Id > lastId)
                .OrderBy(item => item.Id)
                .Take(RebuildPageSize)
                .Include(item => item.Release).ThenInclude(release => release!.Discs)
                .ThenInclude(link => link.Disc!).ThenInclude(disc => disc.Titles.Where(title => title.Item != null))
                .ThenInclude(title => title.Item)
                .Include(item => item.Release).ThenInclude(release => release!.ReleaseGroups).ThenInclude(link => link.Group)
                .AsSplitQuery()
                .AsNoTracking()
                .ToList();
            if (page.Count == 0)
            {
                yield break;
            }

            foreach (var item in page)
            {
                foreach (var entry in item.ToSearchEntries())
                {
                    yield return entry;
                }
            }
            lastId = page[^1].Id;
        }
    }

    private IEnumerable<SearchEntry> BuildMediaItemIndex(SqlServerDataContext dbContext)
    {
        int lastId = 0;
        while (true)
        {
            var page = dbContext.MediaItems
                .Where(item => item.Id > lastId)
                .OrderBy(item => item.Id)
                .Take(RebuildPageSize)
                .Include(item => item.Externalids)
                .Include(item => item.MediaItemGroups).ThenInclude(link => link.Group)
                .Include(item => item.Releases).ThenInclude(release => release.Discs)
                .ThenInclude(link => link.Disc!).ThenInclude(disc => disc.Titles.Where(title => title.Item != null))
                .ThenInclude(title => title.Item)
                .Include(item => item.Releases).ThenInclude(release => release.ReleaseGroups).ThenInclude(link => link.Group)
                .AsSplitQuery()
                .AsNoTracking()
                .ToList();
            if (page.Count == 0)
            {
                yield break;
            }

            foreach (var item in page)
            {
                foreach (var entry in item.ToSearchEntries())
                {
                    yield return entry;
                }
            }
            lastId = page[^1].Id;
        }
    }
}
