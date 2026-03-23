using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using TheDiscDb.Web.Data;

namespace TheDiscDb.Web.Authentication;

public class ApiKeyManager
{
    private readonly IDbContextFactory<SqlServerDataContext> dbContextFactory;
    private readonly IMemoryCache cache;

    private static readonly TimeSpan CacheDuration = TimeSpan.FromMinutes(5);

    public ApiKeyManager(
        IDbContextFactory<SqlServerDataContext> dbContextFactory,
        IMemoryCache cache)
    {
        this.dbContextFactory = dbContextFactory;
        this.cache = cache;
    }

    public async Task<ApiKey?> TryLookupByKeyAsync(string plainTextKey)
    {
        var keyHash = ApiKey.HashKey(plainTextKey);
        var cacheKey = HashCacheKey(keyHash);

        if (cache.TryGetValue(cacheKey, out ApiKey? cached))
        {
            return cached;
        }

        await using var db = await dbContextFactory.CreateDbContextAsync();
        var key = await db.ApiKeys
            .AsNoTracking()
            .FirstOrDefaultAsync(k => k.KeyHash == keyHash && k.IsActive);

        if (key != null)
        {
            SetCache(key, keyHash);
        }

        return key;
    }

    public async Task<ApiKey?> TryGetByIdAsync(int id)
    {
        var cacheKey = IdCacheKey(id);

        if (cache.TryGetValue(cacheKey, out ApiKey? cached))
        {
            return cached;
        }

        await using var db = await dbContextFactory.CreateDbContextAsync();
        var key = await db.ApiKeys
            .AsNoTracking()
            .FirstOrDefaultAsync(k => k.Id == id && k.IsActive);

        if (key != null)
        {
            SetCache(key, key.KeyHash);
        }

        return key;
    }

    private void SetCache(ApiKey key, string keyHash)
    {
        cache.Set(HashCacheKey(keyHash), key, CacheDuration);
        cache.Set(IdCacheKey(key.Id), key, CacheDuration);
    }

    private static string HashCacheKey(string keyHash) => $"apikey:{keyHash}";
    private static string IdCacheKey(int id) => $"apikey-id:{id}";
}
