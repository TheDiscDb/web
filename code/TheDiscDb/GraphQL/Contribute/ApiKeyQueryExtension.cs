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
                Id = k.Id,
                Name = k.Name,
                KeyPrefix = k.KeyPrefix,
                IsActive = k.IsActive,
                Roles = k.Roles,
                CreatedAt = k.CreatedAt,
                ExpiresAt = k.ExpiresAt,
                LastUsedAt = k.LastUsedAt
            });
    }
}

public class ApiKeyInfo
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string KeyPrefix { get; set; } = string.Empty;
    public bool IsActive { get; set; }
    public string? Roles { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? ExpiresAt { get; set; }
    public DateTimeOffset? LastUsedAt { get; set; }
}
