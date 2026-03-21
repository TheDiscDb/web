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
        int id,
        SqlServerDataContext database,
        IMemoryCache cache,
        CancellationToken cancellationToken)
    {
        var apiKey = await database.ApiKeys.FindAsync([id], cancellationToken);
        if (apiKey == null)
        {
            throw new ApiKeyNotFoundException(id);
        }

        apiKey.IsActive = false;
        await database.SaveChangesAsync(cancellationToken);

        cache.Remove($"apikey:{apiKey.KeyHash}");

        return new ApiKeyInfo
        {
            Id = apiKey.Id,
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
