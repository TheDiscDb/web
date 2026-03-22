using HotChocolate.Authorization;
using HotChocolate.Types;
using TheDiscDb.Web.Data;

namespace TheDiscDb.GraphQL.Contribute;

[ExtendObjectType(typeof(ContributionQuery))]
public class ApiKeyQueryExtension
{
    [Authorize("Admin")]
    [UsePaging(MaxPageSize = 100, DefaultPageSize = 50)]
    [UseFiltering]
    [UseSorting]
    public IQueryable<ApiKeyInfo> GetApiKeys(SqlServerDataContext database)
    {
        return database.ApiKeys
            .Select(k => new ApiKeyInfo
            {
                Name = k.Name,
                KeyPrefix = k.KeyPrefix,
                IsActive = k.IsActive,
                Roles = k.Roles,
                OwnerEmail = k.OwnerEmail,
                CreatedAt = k.CreatedAt,
                ExpiresAt = k.ExpiresAt,
                LastUsedAt = k.LastUsedAt
            });
    }

    [Authorize("Admin")]
    [UsePaging(MaxPageSize = 200, DefaultPageSize = 50)]
    [UseFiltering]
    [UseSorting]
    public IQueryable<ApiKeyUsageLogInfo> GetApiKeyUsageLogs(SqlServerDataContext database)
    {
        return database.ApiKeyUsageLogs
            .Select(l => new ApiKeyUsageLogInfo
            {
                ApiKeyPrefix = l.ApiKey.KeyPrefix,
                ApiKeyName = l.ApiKey.Name,
                Timestamp = l.Timestamp,
                OperationName = l.OperationName,
                FieldCost = l.FieldCost,
                TypeCost = l.TypeCost,
                DurationMs = l.DurationMs
            });
    }
}

public class ApiKeyInfo
{
    public string Name { get; set; } = string.Empty;
    public string KeyPrefix { get; set; } = string.Empty;
    public bool IsActive { get; set; }
    public string? Roles { get; set; }
    public string OwnerEmail { get; set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? ExpiresAt { get; set; }
    public DateTimeOffset? LastUsedAt { get; set; }
}

public class ApiKeyUsageLogInfo
{
    public string ApiKeyPrefix { get; set; } = string.Empty;
    public string ApiKeyName { get; set; } = string.Empty;
    public DateTimeOffset Timestamp { get; set; }
    public string? OperationName { get; set; }
    public double FieldCost { get; set; }
    public double TypeCost { get; set; }
    public int DurationMs { get; set; }
}
