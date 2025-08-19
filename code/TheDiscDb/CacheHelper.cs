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

    public async Task<MediaItem?> GetMediaItemDetail(string type, string slug, CancellationToken cancellationToken = default)
    {
        string cacheKey = $"MediaItemDetail|{type}|{slug}";

        return await this.cache.GetOrCreateAsync<MediaItem>(cacheKey, async entry =>
        {
            var context = await this.context.CreateDbContextAsync();
            entry.AbsoluteExpirationRelativeToNow = TimeSpan.FromHours(12);

#pragma warning disable CS8603 // Possible null reference return.
            return await context.MediaItems
                .Include(i => i.Releases.OrderBy(r => r.ReleaseDate))
                .Include("Releases.Discs")
                .Include("Releases.Discs.Titles")
                .Include("Releases.Discs.Titles.Item")
                .Include("MediaItemGroups")
                .Include("MediaItemGroups.Group")
                .AsSplitQuery()
                .FirstOrDefaultAsync(i => i.Type == type && i.Slug == slug, cancellationToken);
#pragma warning restore CS8603 // Possible null reference return.
        });
    }
}