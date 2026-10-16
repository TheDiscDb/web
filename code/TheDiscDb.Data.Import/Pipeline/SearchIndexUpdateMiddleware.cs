namespace TheDiscDb.Data.Import.Pipeline;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using TheDiscDb.InputModels;
using TheDiscDb.Search;
using TheDiscDb.Web.Data;

public class SearchIndexUpdateMiddleware : IMiddleware
{
    private readonly ISearchIndexService searchIndexService;
    private readonly ILogger<SearchIndexUpdateMiddleware> logger;
    private readonly IDbContextFactory<SqlServerDataContext>? dbFactory;

    public SearchIndexUpdateMiddleware(ISearchIndexService searchIndexService)
        : this(searchIndexService, NullLogger<SearchIndexUpdateMiddleware>.Instance)
    {
    }

    public SearchIndexUpdateMiddleware(ISearchIndexService searchIndexService, ILogger<SearchIndexUpdateMiddleware> logger)
    {
        this.searchIndexService = searchIndexService ?? throw new ArgumentNullException(nameof(searchIndexService));
        this.logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public SearchIndexUpdateMiddleware(ISearchIndexService searchIndexService, ILogger<SearchIndexUpdateMiddleware> logger,
        IDbContextFactory<SqlServerDataContext> dbFactory) : this(searchIndexService, logger)
    {
        this.dbFactory = dbFactory;
    }

    public Func<ImportItem, CancellationToken, Task> Next { get; set; } = (_, _) => Task.CompletedTask;

    public async Task Process(ImportItem item, CancellationToken cancellationToken)
    {
        if (item.ReleaseScope != null)
        {
            await this.IndexNewRelease(item, item.ReleaseScope, cancellationToken);
        }
        else if (item.MediaItem != null)
        {
            var searchEntries = item.MediaItem.ToSearchEntries();
            var summary = await this.searchIndexService.IndexItems(searchEntries);
            RecordSummary(item, summary);
            this.LogSummary(summary, "MediaItem", item.MediaItem.Slug, item.MediaItem.Title);
        }
        else if (item.Boxset != null)
        {
            var searchEntries = item.Boxset.ToSearchEntries();
            var summary = await this.searchIndexService.IndexItems(searchEntries);
            RecordSummary(item, summary);
            this.LogSummary(summary, "Boxset", item.Boxset.Slug, item.Boxset.Title);
        }

        await this.Next(item, cancellationToken);
    }

    private async Task IndexNewRelease(ImportItem item, ReleaseImportScope scope, CancellationToken cancellationToken)
    {
        if (this.dbFactory == null)
        {
            throw new InvalidOperationException("Scoped search indexing requires a database context factory.");
        }

        await using var database = await this.dbFactory.CreateDbContextAsync(cancellationToken);
        var parent = await database.MediaItems.AsNoTracking()
            .Include(value => value.Externalids)
            .Include(value => value.MediaItemGroups).ThenInclude(link => link.Group)
            .AsSplitQuery()
            .SingleAsync(value => value.Id == scope.MediaItemId, cancellationToken);
        parent.Releases = await database.Releases.AsNoTracking()
            .Where(release => release.MediaItem!.Id == parent.Id)
            .Select(release => new Release { Upc = release.Upc, Asin = release.Asin })
            .ToListAsync(cancellationToken);
        var entries = item.MediaItem.ToSearchEntries().ToList();
        entries[0] = parent.ToSearchEntry();
        var summary = await this.searchIndexService.IndexItems(entries);
        RecordSummary(item, summary);
        this.LogSummary(summary, "MediaItem", parent.Slug, parent.Title);
        if (!summary.Success)
        {
            return;
        }

        var updates = new Dictionary<string, Dictionary<string, object>>(StringComparer.Ordinal);
        var indexedKeys = entries.Select(entry => entry.id!).ToHashSet(StringComparer.Ordinal);
        if (!scope.OriginalParentGroups.SetEquals(parent.CollectGroupNames()))
        {
            var releases = await database.Releases.AsNoTracking()
                .Where(release => release.MediaItem!.Id == parent.Id && release.Slug != scope.ReleaseSlug)
                .OrderBy(release => release.Id)
                .Select(release => new
                {
                    release.Id,
                    release.Slug,
                    Groups = release.ReleaseGroups.Select(link => link.Group!.Name).ToList()
                }).ToListAsync(cancellationToken);
            var discs = await database.ReleaseDiscs.AsNoTracking()
                .Where(link => link.Release!.MediaItem!.Id == parent.Id && link.Release.Slug != scope.ReleaseSlug)
                .Select(link => new
                {
                    link.Id,
                    ReleaseId = link.Release!.Id,
                    TitleIds = link.Disc!.Titles.Where(title => title.Item != null).Select(title => title.Id).ToList()
                }).ToListAsync(cancellationToken);

            var discsByRelease = discs.ToLookup(disc => disc.ReleaseId);
            foreach (var release in releases)
            {
                var groups = parent.CollectGroupNames().Concat(release.Groups)
                    .Where(name => !string.IsNullOrWhiteSpace(name)).Distinct(StringComparer.Ordinal).ToArray();
                AddField(SearchEntryExtensions.ReleaseKey(release.Slug), "Groups", groups);
                foreach (var disc in discsByRelease[release.Id])
                {
                    AddField(SearchEntryExtensions.DiscKey(disc.Id), "Groups", groups);
                    foreach (var titleId in disc.TitleIds)
                    {
                        AddField(SearchEntryExtensions.TitleKey(titleId), "Groups", groups);
                    }
                }
            }
        }

        if (scope.ChangedTitleIds.Count > 0)
        {
            var changedIds = scope.ChangedTitleIds.ToArray();
            var titles = await database.Titles.AsNoTracking()
                .Where(title => changedIds.Contains(title.Id) && title.Item != null)
                .Select(title => new { title.Id, title.Item!.Title, title.Item.Type })
                .ToListAsync(cancellationToken);
            foreach (var title in titles)
            {
                AddField(SearchEntryExtensions.TitleKey(title.Id), "Title", title.Title!);
                AddField(SearchEntryExtensions.TitleKey(title.Id), "Type", title.Type!);
            }

            var mappedIds = titles.Select(title => title.Id).ToHashSet();
            var deletedKeys = changedIds.Where(id => !mappedIds.Contains(id))
                .Select(SearchEntryExtensions.TitleKey).Where(key => !indexedKeys.Contains(key)).ToList();
            if (deletedKeys.Count > 0)
            {
                foreach (var key in deletedKeys)
                {
                    updates.Remove(key);
                }
                await this.searchIndexService.DeleteItems(deletedKeys);
            }
        }

        if (updates.Count > 0)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var refresh = await this.searchIndexService.MergeItems(updates.Select(update =>
                new SearchDocumentUpdate(update.Key, update.Value)));
            RecordSummary(item, refresh, siblingRefresh: true);
            this.LogSummary(refresh, "Sibling search fields", parent.Slug, parent.Title);
        }

        void AddField(string key, string field, object value)
        {
            if (indexedKeys.Contains(key))
            {
                return;
            }

            if (!updates.TryGetValue(key, out var fields))
            {
                fields = new Dictionary<string, object>(StringComparer.Ordinal);
                updates.Add(key, fields);
            }
            fields[field] = value;
        }
    }

    private static void RecordSummary(ImportItem item, BuildIndexSummary summary, bool siblingRefresh = false)
    {
        var failure = summary.Success ? null : new InvalidOperationException(
            $"Search indexing failed: {summary.ErrorMessage ?? "The search service returned no error details."}");
        if (item.Diagnostics != null)
        {
            item.Diagnostics.Failed |= !summary.Success;
            if (!summary.Success)
            {
                item.Diagnostics.FailedStages.Add(nameof(SearchIndexUpdateMiddleware));
                item.Diagnostics.FailureException ??= failure;
            }
            if (siblingRefresh)
            {
                item.Diagnostics.RefreshedSiblingDocuments += summary.ItemCount;
            }
            else
            {
                item.Diagnostics.IndexedDocuments += summary.ItemCount;
            }
        }
        if (failure != null && item.ReleaseScope != null)
        {
            throw failure;
        }
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
