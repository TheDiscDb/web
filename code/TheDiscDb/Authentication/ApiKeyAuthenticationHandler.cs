using System.Net;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;
using TheDiscDb.Web.Data;

namespace TheDiscDb.Web.Authentication;

public class ApiKeyAuthenticationHandler : AuthenticationHandler<ApiKeyAuthenticationOptions>
{
    private readonly IDbContextFactory<SqlServerDataContext> dbContextFactory;
    private readonly IMemoryCache cache;

    private static readonly TimeSpan CacheDuration = TimeSpan.FromMinutes(5);

    public ApiKeyAuthenticationHandler(
        IOptionsMonitor<ApiKeyAuthenticationOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder,
        IDbContextFactory<SqlServerDataContext> dbContextFactory,
        IMemoryCache cache)
        : base(options, logger, encoder)
    {
        this.dbContextFactory = dbContextFactory;
        this.cache = cache;
    }

    protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (!Options.IsEnabled)
        {
            return AuthenticateResult.NoResult();
        }

        //if (IsLoopbackRequest())
        //{
        //    var localClaims = new[] { new Claim(ClaimTypes.Name, "local-internal") };
        //    var localIdentity = new ClaimsIdentity(localClaims, ApiKeyAuthenticationDefaults.Scheme);
        //    var localPrincipal = new ClaimsPrincipal(localIdentity);
        //    var localTicket = new AuthenticationTicket(localPrincipal, ApiKeyAuthenticationDefaults.Scheme);
        //    return AuthenticateResult.Success(localTicket);
        //}

        var authHeader = Request.Headers.Authorization.ToString();
        if (string.IsNullOrEmpty(authHeader) || !authHeader.StartsWith($"{ApiKeyAuthenticationDefaults.Scheme} ", StringComparison.OrdinalIgnoreCase))
        {
            return AuthenticateResult.NoResult();
        }

        var apiKey = authHeader[$"{ApiKeyAuthenticationDefaults.Scheme} ".Length..].Trim();
        if (string.IsNullOrEmpty(apiKey))
        {
            return AuthenticateResult.Fail("API key is empty.");
        }

        var key = await TryLookupApiKey(apiKey);

        if (key == null)
        {
            return AuthenticateResult.Fail("Invalid API key.");
        }

        if (key.ExpiresAt.HasValue && key.ExpiresAt.Value < DateTimeOffset.UtcNow)
        {
            return AuthenticateResult.Fail("API key has expired.");
        }

        _ = UpdateLastUsedAsync(key.Id).ContinueWith(t =>
        {
            this.Logger.LogWarning(t.Exception, "Failed to update LastUsedAt for API key {KeyId}", key.Id);
        }, TaskContinuationOptions.OnlyOnFaulted);

        var claims = new List<Claim>
        {
            new Claim(ClaimTypes.Name, key.Name),
            new Claim("ApiKeyId", key.Id.ToString()),
            new Claim(ClaimTypes.AuthenticationMethod, ApiKeyAuthenticationDefaults.Scheme)
        };

        if (!string.IsNullOrEmpty(key.Roles))
        {
            foreach (var role in key.Roles.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                claims.Add(new Claim(ClaimTypes.Role, role));
            }
        }

        var identity = new ClaimsIdentity(claims, ApiKeyAuthenticationDefaults.Scheme);
        var principal = new ClaimsPrincipal(identity);
        var ticket = new AuthenticationTicket(principal, ApiKeyAuthenticationDefaults.Scheme);

        return AuthenticateResult.Success(ticket);
    }

    private async Task<ApiKey?> TryLookupApiKey(string apiKey)
    {
        var keyHash = ApiKey.HashKey(apiKey);
        var cacheKey = $"apikey:{keyHash}";

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
            cache.Set(cacheKey, key, CacheDuration);
        }

        return key;
    }

    private bool IsLoopbackRequest()
    {
        var connection = Context.Connection;
        if (connection.RemoteIpAddress == null)
        {
            return false;
        }

        return IPAddress.IsLoopback(connection.RemoteIpAddress);
    }

    private async Task UpdateLastUsedAsync(int keyId)
    {
        try
        {
            await using var db = await dbContextFactory.CreateDbContextAsync();
            await db.ApiKeys
                .Where(k => k.Id == keyId)
                .ExecuteUpdateAsync(s => s.SetProperty(k => k.LastUsedAt, DateTimeOffset.UtcNow));
        }
        catch (Exception ex)
        {
            Logger.LogWarning(ex, "Failed to update LastUsedAt for API key {KeyId}", keyId);
        }
    }
}
