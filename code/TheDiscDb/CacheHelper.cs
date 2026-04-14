using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using TheDiscDb.InputModels;
using TheDiscDb.Web.Data;

namespace TheDiscDb;

public class CacheHelper
{
    private readonly IDbContextFactory<SqlServerDataContext> context;
    private readonly IMemoryCache cache;

    public CacheHelper(IDbContextFactory<SqlServerDataContext> context, IMemoryCache cache)
    {
        this.context = context ?? throw new ArgumentNullException(nameof(context));
        this.cache = cache ?? throw new ArgumentNullException(nameof(cache));
    }

    public async Task<Boxset?> GetBoxsetAsync(string slug, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(slug))
        {
            return null;
        }

        string normalizedSlug = slug.ToLower();
        string cacheKey = $"Boxset|{normalizedSlug}";
        return await this.cache.GetOrCreateAsync<Boxset>(cacheKey, async entry =>
        {
            await using var context = await this.context.CreateDbContextAsync();
            entry.AbsoluteExpirationRelativeToNow = TimeSpan.FromHours(12);

#pragma warning disable CS8603 // Possible null reference return.
            var boxset = await context.BoxSets
            .Include("Release")
            .Include("Release.Discs")
            .Include("Release.Discs.Titles")
            .Include("Release.Discs.Titles.Item")
            .Include("Release.Discs.Titles.Item.Chapters")
            .Include("Release.Discs.Titles.Tracks")
            .AsSplitQuery()
            .FirstOrDefaultAsync(i => i.Slug == slug, cancellationToken);
            return boxset;
#pragma warning restore CS8603 // Possible null reference return.
        });
    }

    public async Task<MediaItem?> GetMediaItemDetail(string type, string slug, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(slug))
        {
            return null;
        }

        string normalizedType = type.ToLower();
        string normalizedSlug = slug.ToLower();
        string cacheKey = $"MediaItemDetail|{normalizedType}|{normalizedSlug}";

        return await this.cache.GetOrCreateAsync<MediaItem>(cacheKey, async entry =>
        {
            await using var context = await this.context.CreateDbContextAsync();
            entry.AbsoluteExpirationRelativeToNow = TimeSpan.FromHours(12);

#pragma warning disable CS8603 // Possible null reference return.
            return await context.MediaItems
                .Include(i => i.Releases.OrderBy(r => r.ReleaseDate))
                .Include("Releases.Discs")
                .Include("Releases.Discs.Titles")
                .Include("Releases.Discs.Titles.Item")
                .Include("Releases.Discs.Titles.Item.Chapters")
                .Include("Releases.Discs.Titles.Tracks")
                .Include("Releases.Contributors")
                .Include("Releases.ReleaseGroups")
                .Include("Releases.ReleaseGroups.Group")
                .Include("MediaItemGroups")
                .Include("MediaItemGroups.Group")
                .AsSplitQuery()
                .FirstOrDefaultAsync(i => i.Type == type && i.Slug == normalizedSlug, cancellationToken);
#pragma warning restore CS8603 // Possible null reference return.
        });
    }
}