using System.Collections.Concurrent;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using TheDiscDb.InputModels;
using TheDiscDb.Web.Data;

namespace TheDiscDb.Components.Pages;

/// <summary>
/// Helpers for the crawler-only SSR browse pages (/movies, /series, /boxsets, /g/{slug}).
///
/// Each helper queries the full list, projects to the minimal shape needed for a
/// media-item-card, and caches the projection in <see cref="IMemoryCache"/> for a
/// short TTL so repeat crawler hits don't re-run the query. Real users get the
/// WASM InfiniteScrolling component, so they never hit these helpers.
/// </summary>
internal static class CrawlerBrowseSsr
{
    // Crawlers re-fetch the same listing repeatedly. 15 minutes mirrors the
    // SitemapMiddleware's TTL — fresh enough that newly added items appear
    // quickly, slow enough that bursty bot traffic doesn't hammer the DB.
    public static readonly TimeSpan CacheDuration = TimeSpan.FromMinutes(15);

    // Per-key single-flight: when the cache misses, the first caller starts the
    // query and any concurrent callers await the same Task instead of each running
    // their own SELECT. Without this, a swarm of crawlers arriving right after
    // cache expiry could all kick off independent full-table scans.
    private static readonly ConcurrentDictionary<string, Lazy<Task<object>>> InFlight = new();

    public static Task<IReadOnlyList<MediaItem>> GetMoviesAsync(
        IDbContextFactory<SqlServerDataContext> contextFactory,
        IMemoryCache cache,
        CancellationToken cancellationToken)
        => GetOrCreateMediaItemsAsync(contextFactory, cache, "ssr-movies", "Movie", cancellationToken);

    public static Task<IReadOnlyList<MediaItem>> GetSeriesAsync(
        IDbContextFactory<SqlServerDataContext> contextFactory,
        IMemoryCache cache,
        CancellationToken cancellationToken)
        => GetOrCreateMediaItemsAsync(contextFactory, cache, "ssr-series", "Series", cancellationToken);

    public static Task<IReadOnlyList<Boxset>> GetBoxsetsAsync(
        IDbContextFactory<SqlServerDataContext> contextFactory,
        IMemoryCache cache,
        CancellationToken cancellationToken)
        => SingleFlightAsync<IReadOnlyList<Boxset>>("ssr-boxsets", cache, cancellationToken, async ct =>
        {
            await using var db = await contextFactory.CreateDbContextAsync(ct);
            return await db.BoxSets
                .AsNoTracking()
                .OrderBy(b => b.SortTitle ?? b.Title)
                .Select(b => new Boxset
                {
                    Slug = b.Slug,
                    Title = b.Title,
                    ImageUrl = b.ImageUrl,
                })
                .ToListAsync(ct);
        });

    private static Task<IReadOnlyList<MediaItem>> GetOrCreateMediaItemsAsync(
        IDbContextFactory<SqlServerDataContext> contextFactory,
        IMemoryCache cache,
        string cacheKey,
        string mediaType,
        CancellationToken cancellationToken)
        => SingleFlightAsync<IReadOnlyList<MediaItem>>(cacheKey, cache, cancellationToken, async ct =>
        {
            await using var db = await contextFactory.CreateDbContextAsync(ct);
            return await db.MediaItems
                .AsNoTracking()
                .Where(i => i.Type == mediaType)
                .OrderBy(i => i.SortTitle ?? i.Title)
                .ThenBy(i => i.Year)
                .Select(i => new MediaItem
                {
                    Slug = i.Slug,
                    Title = i.Title,
                    Year = i.Year,
                    Type = i.Type,
                    ImageUrl = i.ImageUrl,
                })
                .ToListAsync(ct);
        });

    /// <summary>
    /// Returns the cached value, or — on miss — runs <paramref name="factory"/>
    /// once across all concurrent callers and caches the result. The factory
    /// runs detached from any single caller's CancellationToken so that the
    /// first crawler disconnecting does not abort the population for everyone
    /// else still waiting; individual callers honor their own token while
    /// awaiting the shared task.
    /// </summary>
    private static async Task<T> SingleFlightAsync<T>(
        string cacheKey,
        IMemoryCache cache,
        CancellationToken cancellationToken,
        Func<CancellationToken, Task<T>> factory)
        where T : class
    {
        if (cache.TryGetValue(cacheKey, out T? hit) && hit is not null)
        {
            return hit;
        }

        var lazy = InFlight.GetOrAdd(cacheKey, _ => new Lazy<Task<object>>(async () =>
        {
            try
            {
                // Detached token: the first caller's cancellation must not
                // poison the cache fill for every other waiter.
                var value = await factory(CancellationToken.None).ConfigureAwait(false);
                cache.Set(cacheKey, value, CacheDuration);
                return value!;
            }
            finally
            {
                InFlight.TryRemove(cacheKey, out Lazy<Task<object>>? _);
            }
        }));

        var task = lazy.Value;

        // Allow the individual caller to bail if their request is aborted,
        // even though the underlying fill keeps running for other waiters.
        var completed = await Task.WhenAny(task, Task.Delay(Timeout.Infinite, cancellationToken))
            .ConfigureAwait(false);

        if (completed != task)
        {
            cancellationToken.ThrowIfCancellationRequested();
        }

        return (T)await task.ConfigureAwait(false);
    }
}

