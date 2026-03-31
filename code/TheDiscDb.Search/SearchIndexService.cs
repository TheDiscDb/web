using System.Diagnostics;
using Azure.Search.Documents.Indexes;
using Azure.Search.Documents.Indexes.Models;
using Azure.Search.Documents.Models;
using Microsoft.EntityFrameworkCore;
using TheDiscDb.InputModels;
using TheDiscDb.Web.Data;

namespace TheDiscDb.Search;

public class SearchIndexService : ISearchIndexService
{
    private readonly SearchIndexClient client;
    private readonly IDbContextFactory<SqlServerDataContext> dbFactory;
    private static readonly string IndexName = "all-items";

    public SearchIndexService(SearchIndexClient client, IDbContextFactory<SqlServerDataContext> dbFactory)
    {
        this.client = client ?? throw new ArgumentNullException(nameof(client));
        this.dbFactory = dbFactory ?? throw new ArgumentNullException(nameof(dbFactory));
    }

    public async Task<BuildIndexSummary> IndexItems(IEnumerable<SearchEntry> entries, int batchSize = 10)
    {
        var stopwatch = Stopwatch.StartNew();

        var searchClient = this.client.GetSearchClient(IndexName);
        var result = new BuildIndexSummary
        {
            Success = true
        };

        foreach (var batch in entries.Batch(BatchSize))
        {
            try
            {
                var a = batch.Select(b => IndexDocumentsAction.MergeOrUpload(b));
                var searchEntryBatch = IndexDocumentsBatch.Create(a.ToArray());
                var itemResult = await searchClient.IndexDocumentsAsync(searchEntryBatch);

                if (!itemResult.Value.Results.All(r => r.Succeeded))
                {
                    result.Success = false;

                    // Try returning the first error message you find
                    if (string.IsNullOrEmpty(result.ErrorMessage))
                    {
                        var errorItem = itemResult.Value.Results.FirstOrDefault(r => !string.IsNullOrEmpty(r.ErrorMessage));
                        if (errorItem != null)
                        {
                            result.ErrorMessage = errorItem.ErrorMessage;
                        }
                    }
                }

                result.ItemCount += itemResult.Value.Results.Count;
            }
            catch (Exception e)
            {
                result.Success = false;
                result.ErrorMessage = e.Message;
            }
        }

        stopwatch.Stop();
        result.Duration = stopwatch.Elapsed;

        return result;
    }

    public async Task<BuildIndexSummary> BuildIndex()
    {
        BuildIndexSummary summary = new();
        var stopwatch = Stopwatch.StartNew();

        var index = new SearchIndex(IndexName, this.GetIndexFields());
        await this.client.CreateOrUpdateIndexAsync(index);

        using (var dbContext = this.dbFactory.CreateDbContext())
        {
            var searchClient = this.client.GetSearchClient(IndexName);

            var actions = this.BuildMediaItemIndex(dbContext);
            var mediaItemresult = await this.IndexItems(actions, BatchSize);
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
            var boxsetResult = await this.IndexItems(boxsetActions, BatchSize);
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

    private const int BatchSize = 10;

    private IEnumerable<SearchEntry> BuildBoxsetIndex(SqlServerDataContext dbContext)
    {
#pragma warning disable CS8602 // Dereference of a possibly null reference.
        foreach (var item in dbContext.BoxSets
            .Include(p => p.Release)
            .ThenInclude(r => r.Discs)
            .ThenInclude(d => d.Titles)
            .ThenInclude(t => t.Item)
            )
#pragma warning restore CS8602 // Dereference of a possibly null reference.
        {
            var searchItem = new SearchEntry
            {
                id = string.Join('-', item.Id, "Boxset"),
                Type = "Boxset",
                Title = item.Title,
                ImageUrl = item.ImageUrl,
                RelativeUrl = $"/boxset/{item.Slug}",
                Identifiers = CollectIdentifiers(release: item.Release),
                MediaItem = new ItemInfo
                {
                    Slug = item.Slug,
                    ImageUrl = item.ImageUrl
                }
            };

            yield return searchItem;

            if (item?.Release == null)
            {
                yield break;
            }

            foreach (var disc in item.Release.Discs)
            {
                searchItem = new SearchEntry
                {
                    id = string.Join('-', "BoxsetDisc", disc.SlugOrIndex()),
                    Type = "BoxsetDisc",
                    Title = disc.Name,
                    ImageUrl = item.ImageUrl,
                    RelativeUrl = $"/boxset/{item.Slug}/discs/{disc.Index}",
                    MediaItem = new ItemInfo
                    {
                        Slug = item.Slug,
                        ImageUrl = item.ImageUrl
                    },
                    Release = new ItemInfo
                    {
                        Slug = item.Release.Slug,
                        ImageUrl = item.Release.ImageUrl
                    },
                    Disc = new ItemInfo
                    {
                        Slug = disc.Slug
                    }
                };

                yield return searchItem;

                foreach (var title in disc.Titles)
                {
                    if (title.Item != null)
                    {
                        searchItem = new SearchEntry
                        {
                            id = string.Join('-', "BoxsetTitle", item.Slug, disc.SlugOrIndex(), title.SegmentMap, title.SourceFile),
                            Type = title.Item.Type,
                            Title = title.Item.Title,
                            ImageUrl = item.ImageUrl,
                            RelativeUrl = $"/boxset/{item.Slug}/discs/{disc.Index}",
                            MediaItem = new ItemInfo
                            {
                                Slug = item.Slug,
                                ImageUrl = item.ImageUrl
                            },
                            Release = new ItemInfo
                            {
                                Slug = item.Release.Slug,
                                ImageUrl = item.Release.ImageUrl
                            },
                            Disc = new ItemInfo
                            {
                                Slug = disc.Slug
                            }
                        };

                        yield return searchItem;
                    }
                }
            }
        }
    }

    private IEnumerable<SearchEntry> BuildMediaItemIndex(SqlServerDataContext dbContext)
    {
        List<SearchEntry> actions = new();
        foreach (var item in dbContext.MediaItems
            .Include(p => p.Externalids)
            .Include(p => p.Releases)
            .ThenInclude(r => r.Discs)
            .ThenInclude(d => d.Titles)
            .ThenInclude(t => t.Item)
            )
        {
            actions.AddRange(item.ToSearchEntries());
        }

        return actions;
    }

    private static IList<string> CollectIdentifiers(
        InputModels.ExternalIds? externalIds = null,
        IEnumerable<InputModels.Release>? releases = null,
        InputModels.Release? release = null)
    {
        var ids = new List<string>();

        if (externalIds != null)
        {
            if (!string.IsNullOrWhiteSpace(externalIds.Imdb)) ids.Add(externalIds.Imdb);
            if (!string.IsNullOrWhiteSpace(externalIds.Tmdb)) ids.Add(externalIds.Tmdb);
            if (!string.IsNullOrWhiteSpace(externalIds.Tvdb)) ids.Add(externalIds.Tvdb);
        }

        if (releases != null)
        {
            foreach (var r in releases)
            {
                if (!string.IsNullOrWhiteSpace(r.Upc)) ids.Add(r.Upc);
                if (!string.IsNullOrWhiteSpace(r.Asin)) ids.Add(r.Asin);
            }
        }

        if (release != null)
        {
            if (!string.IsNullOrWhiteSpace(release.Upc)) ids.Add(release.Upc);
            if (!string.IsNullOrWhiteSpace(release.Asin)) ids.Add(release.Asin);
        }

        return ids;
    }
}
