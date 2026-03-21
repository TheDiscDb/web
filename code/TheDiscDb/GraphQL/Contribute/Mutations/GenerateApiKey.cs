using System.Security.Cryptography;
using HotChocolate.Authorization;
using Microsoft.EntityFrameworkCore;
using TheDiscDb.GraphQL.Contribute.Exceptions;
using TheDiscDb.Web.Authentication;
using TheDiscDb.Web.Data;

namespace TheDiscDb.GraphQL.Contribute.Mutations;

public partial class ContributionMutations
{
    [Authorize("Admin")]
    public async Task<GenerateApiKeyPayload> GenerateApiKey(
        string name,
        DateTimeOffset? expiresAt,
        SqlServerDataContext database,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ArgumentException("API key name is required.", nameof(name));
        }

        var keyBytes = RandomNumberGenerator.GetBytes(32);
        var plainTextKey = Convert.ToBase64String(keyBytes)
            .Replace("+", "-")
            .Replace("/", "_")
            .TrimEnd('=');

        var keyHash = ApiKeyAuthenticationHandler.HashKey(plainTextKey);
        var keyPrefix = plainTextKey[..8];

        var apiKey = new ApiKey
        {
            Name = name,
            KeyHash = keyHash,
            KeyPrefix = keyPrefix,
            IsActive = true,
            CreatedAt = DateTimeOffset.UtcNow,
            ExpiresAt = expiresAt
        };

        database.ApiKeys.Add(apiKey);
        await database.SaveChangesAsync(cancellationToken);

        return new GenerateApiKeyPayload(apiKey.Id, plainTextKey, keyPrefix, name);
    }
}

public record GenerateApiKeyPayload(int Id, string Key, string KeyPrefix, string Name);
