using HotChocolate.Authorization;
using TheDiscDb.Web.Data;

namespace TheDiscDb.GraphQL.Contribute.Mutations;

public partial class ContributionMutations
{
    [Authorize("Admin")]
    public async Task<GenerateApiKeyPayload> GenerateApiKey(
        string name,
        string[]? roles,
        DateTimeOffset? expiresAt,
        SqlServerDataContext database,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ArgumentException("API key name is required.", nameof(name));
        }

        string plainTextKey = ApiKey.GeneratePlainTextKey();
        var apiKey = ApiKey.Create(plainTextKey, name, roles, expiresAt);

        database.ApiKeys.Add(apiKey);
        await database.SaveChangesAsync(cancellationToken);

        return new GenerateApiKeyPayload(plainTextKey, apiKey.KeyPrefix, name);
    }
}

public record GenerateApiKeyPayload(string Key, string KeyPrefix, string Name);
