using System;
using System.Security.Cryptography;

namespace TheDiscDb.Web.Data;

public class ApiKey
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string KeyHash { get; set; } = string.Empty;
    public string KeyPrefix { get; set; } = string.Empty;
    public bool IsActive { get; set; } = true;
    public string? Roles { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? ExpiresAt { get; set; }
    public DateTimeOffset? LastUsedAt { get; set; }

    public static ApiKey Create(string plainTextKey, string name, string[]? roles = null, DateTimeOffset? expiresAt = null)
    {
        var keyHash = ApiKeyHasher.HashKey(plainTextKey);
        var keyPrefix = plainTextKey.Length >= 8 ? plainTextKey[..8] : plainTextKey;
        var rolesValue = roles is { Length: > 0 } ? string.Join(",", roles) : null;

        return new ApiKey
        {
            Name = name,
            KeyHash = keyHash,
            KeyPrefix = keyPrefix,
            IsActive = true,
            Roles = rolesValue,
            CreatedAt = DateTimeOffset.UtcNow,
            ExpiresAt = expiresAt
        };
    }

    public static string GeneratePlainTextKey()
    {
        var keyBytes = RandomNumberGenerator.GetBytes(32);
        return Convert.ToBase64String(keyBytes)
            .Replace("+", "-")
            .Replace("/", "_")
            .TrimEnd('=');
    }
}
