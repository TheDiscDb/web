using HotChocolate.Authorization;
using TheDiscDb.Web.Data;

namespace TheDiscDb.GraphQL.Contribute.Mutations;

public partial class ContributionMutations
{
    [Authorize("Admin")]
    public async Task<GenerateApiKeyPayload> GenerateApiKey(
        string name,
        string ownerEmail,
        string[]? roles,
        DateTimeOffset? expiresAt,
        SqlServerDataContext database,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ArgumentException("API key name is required.", nameof(name));
        }

        if (string.IsNullOrWhiteSpace(ownerEmail))
        {
            throw new ArgumentException("Owner email is required.", nameof(ownerEmail));
        }

        string plainTextKey = ApiKey.GeneratePlainTextKey();
        var apiKey = ApiKey.Create(plainTextKey, name, ownerEmail, roles, expiresAt);

        database.ApiKeys.Add(apiKey);
        await database.SaveChangesAsync(cancellationToken);

        return new GenerateApiKeyPayload(plainTextKey, apiKey.KeyPrefix, name, ownerEmail);
    }
}

public record GenerateApiKeyPayload(string Key, string KeyPrefix, string Name, string OwnerEmail);
