using HotChocolate.Authorization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using TheDiscDb.GraphQL.Contribute.Exceptions;
using TheDiscDb.Web.Data;

namespace TheDiscDb.GraphQL.Contribute.Mutations;

public partial class ContributionMutations
{
    [Authorize("Admin")]
    [Error(typeof(ApiKeyNotFoundException))]
    public async Task<ApiKeyInfo> RevokeApiKey(
        string keyPrefix,
        SqlServerDataContext database,
        IMemoryCache cache,
        CancellationToken cancellationToken)
    {
        var apiKey = await database.ApiKeys
            .FirstOrDefaultAsync(k => k.KeyPrefix == keyPrefix, cancellationToken);
        if (apiKey == null)
        {
            throw new ApiKeyNotFoundException(keyPrefix);
        }

        apiKey.IsActive = false;
        await database.SaveChangesAsync(cancellationToken);

        cache.Remove($"apikey:{apiKey.KeyHash}");

        return new ApiKeyInfo
        {
            Name = apiKey.Name,
            KeyPrefix = apiKey.KeyPrefix,
            IsActive = apiKey.IsActive,
            Roles = apiKey.Roles,
            CreatedAt = apiKey.CreatedAt,
            ExpiresAt = apiKey.ExpiresAt,
            LastUsedAt = apiKey.LastUsedAt
        };
    }
}
