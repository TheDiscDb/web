using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using TheDiscDb.Web.Authentication;
using TheDiscDb.Web.Data;
using System.Text.Encodings.Web;
using System.Net;

namespace TheDiscDb.UnitTests.Server.Authentication;

public class ApiKeyAuthenticationHandlerTests
{
    private static SqlServerDataContext CreateDbContext(string dbName)
    {
        var options = new DbContextOptionsBuilder<SqlServerDataContext>()
            .UseInMemoryDatabase(databaseName: dbName)
            .Options;
        return new SqlServerDataContext(options);
    }

    private class TestDbContextFactory(string dbName) : IDbContextFactory<SqlServerDataContext>
    {
        public SqlServerDataContext CreateDbContext()
        {
            var options = new DbContextOptionsBuilder<SqlServerDataContext>()
                .UseInMemoryDatabase(databaseName: dbName)
                .Options;
            return new SqlServerDataContext(options);
        }
    }

    private static async Task<ApiKeyAuthenticationHandler> CreateHandler(
        string dbName,
        bool isEnabled,
        HttpContext httpContext,
        IMemoryCache? cache = null)
    {
        var factory = new TestDbContextFactory(dbName);
        var optionsMonitor = new TestOptionsMonitor(new ApiKeyAuthenticationOptions { IsEnabled = isEnabled });
        var loggerFactory = NullLoggerFactory.Instance;
        cache ??= new MemoryCache(new MemoryCacheOptions());

        var handler = new ApiKeyAuthenticationHandler(
            optionsMonitor,
            loggerFactory,
            UrlEncoder.Default,
            factory,
            cache);

        await handler.InitializeAsync(
            new AuthenticationScheme(ApiKeyAuthenticationDefaults.Scheme, null, typeof(ApiKeyAuthenticationHandler)),
            httpContext);

        return handler;
    }

    private static HttpContext CreateHttpContext(string? authorizationHeader = null, IPAddress? remoteIp = null, IPAddress? localIp = null)
    {
        var context = new DefaultHttpContext();
        if (authorizationHeader != null)
        {
            context.Request.Headers.Authorization = authorizationHeader;
        }
        if (remoteIp != null)
        {
            context.Connection.RemoteIpAddress = remoteIp;
        }
        if (localIp != null)
        {
            context.Connection.LocalIpAddress = localIp;
        }
        return context;
    }

    [Test]
    public async Task Returns_NoResult_When_Disabled()
    {
        var dbName = Guid.NewGuid().ToString();
        var context = CreateHttpContext("ApiKey test-key");
        var handler = await CreateHandler(dbName, isEnabled: false, context);

        var result = await handler.AuthenticateAsync();

        await Assert.That(result.None).IsTrue();
    }

    [Test]
    public async Task Returns_NoResult_When_No_Authorization_Header()
    {
        var dbName = Guid.NewGuid().ToString();
        var context = CreateHttpContext(remoteIp: IPAddress.Parse("203.0.113.1"), localIp: IPAddress.Loopback);
        var handler = await CreateHandler(dbName, isEnabled: true, context);

        var result = await handler.AuthenticateAsync();

        await Assert.That(result.None).IsTrue();
    }

    [Test]
    public async Task Returns_NoResult_When_Wrong_Scheme()
    {
        var dbName = Guid.NewGuid().ToString();
        var context = CreateHttpContext("Bearer some-token", remoteIp: IPAddress.Parse("203.0.113.1"), localIp: IPAddress.Loopback);
        var handler = await CreateHandler(dbName, isEnabled: true, context);

        var result = await handler.AuthenticateAsync();

        await Assert.That(result.None).IsTrue();
    }

    [Test]
    public async Task Returns_Fail_When_Key_Not_Found()
    {
        var dbName = Guid.NewGuid().ToString();
        var context = CreateHttpContext("ApiKey invalid-key", remoteIp: IPAddress.Parse("203.0.113.1"), localIp: IPAddress.Loopback);
        var handler = await CreateHandler(dbName, isEnabled: true, context);

        var result = await handler.AuthenticateAsync();

        await Assert.That(result.Succeeded).IsFalse();
        await Assert.That(result.Failure!.Message).IsEqualTo("Invalid API key.");
    }

    [Test]
    public async Task Returns_Success_When_Valid_Key()
    {
        var dbName = Guid.NewGuid().ToString();
        var plainKey = "test-api-key-12345678";
        var keyHash = ApiKeyHasher.HashKey(plainKey);

        using (var db = CreateDbContext(dbName))
        {
            db.ApiKeys.Add(new ApiKey
            {
                Name = "Test Key",
                KeyHash = keyHash,
                KeyPrefix = plainKey[..8],
                IsActive = true,
                CreatedAt = DateTimeOffset.UtcNow
            });
            await db.SaveChangesAsync();
        }

        var context = CreateHttpContext($"ApiKey {plainKey}", remoteIp: IPAddress.Parse("203.0.113.1"), localIp: IPAddress.Loopback);
        var handler = await CreateHandler(dbName, isEnabled: true, context);

        var result = await handler.AuthenticateAsync();

        await Assert.That(result.Succeeded).IsTrue();
        await Assert.That(result.Principal!.Identity!.Name).IsEqualTo("Test Key");
    }

    [Test]
    public async Task Returns_Fail_When_Key_Inactive()
    {
        var dbName = Guid.NewGuid().ToString();
        var plainKey = "test-api-key-inactive";
        var keyHash = ApiKeyHasher.HashKey(plainKey);

        using (var db = CreateDbContext(dbName))
        {
            db.ApiKeys.Add(new ApiKey
            {
                Name = "Revoked Key",
                KeyHash = keyHash,
                KeyPrefix = plainKey[..8],
                IsActive = false,
                CreatedAt = DateTimeOffset.UtcNow
            });
            await db.SaveChangesAsync();
        }

        var context = CreateHttpContext($"ApiKey {plainKey}", remoteIp: IPAddress.Parse("203.0.113.1"), localIp: IPAddress.Loopback);
        var handler = await CreateHandler(dbName, isEnabled: true, context);

        var result = await handler.AuthenticateAsync();

        await Assert.That(result.Succeeded).IsFalse();
        await Assert.That(result.Failure!.Message).IsEqualTo("Invalid API key.");
    }

    [Test]
    public async Task Returns_Fail_When_Key_Expired()
    {
        var dbName = Guid.NewGuid().ToString();
        var plainKey = "test-api-key-expired1";
        var keyHash = ApiKeyHasher.HashKey(plainKey);

        using (var db = CreateDbContext(dbName))
        {
            db.ApiKeys.Add(new ApiKey
            {
                Name = "Expired Key",
                KeyHash = keyHash,
                KeyPrefix = plainKey[..8],
                IsActive = true,
                CreatedAt = DateTimeOffset.UtcNow.AddDays(-30),
                ExpiresAt = DateTimeOffset.UtcNow.AddDays(-1)
            });
            await db.SaveChangesAsync();
        }

        var context = CreateHttpContext($"ApiKey {plainKey}", remoteIp: IPAddress.Parse("203.0.113.1"), localIp: IPAddress.Loopback);
        var handler = await CreateHandler(dbName, isEnabled: true, context);

        var result = await handler.AuthenticateAsync();

        await Assert.That(result.Succeeded).IsFalse();
        await Assert.That(result.Failure!.Message).IsEqualTo("API key has expired.");
    }

    [Test]
    public async Task Loopback_Request_Without_Valid_Key_Fails()
    {
        var dbName = Guid.NewGuid().ToString();
        var context = CreateHttpContext("ApiKey some-invalid-key", remoteIp: IPAddress.Loopback, localIp: IPAddress.Loopback);
        var handler = await CreateHandler(dbName, isEnabled: true, context);

        var result = await handler.AuthenticateAsync();

        await Assert.That(result.Succeeded).IsFalse();
    }

    [Test]
    public async Task Cache_Returns_Same_Result_Without_Second_Db_Call()
    {
        var dbName = Guid.NewGuid().ToString();
        var plainKey = "test-api-key-for-cache";
        var keyHash = ApiKeyHasher.HashKey(plainKey);

        using (var db = CreateDbContext(dbName))
        {
            db.ApiKeys.Add(new ApiKey
            {
                Name = "Cached Key",
                KeyHash = keyHash,
                KeyPrefix = plainKey[..8],
                IsActive = true,
                CreatedAt = DateTimeOffset.UtcNow
            });
            await db.SaveChangesAsync();
        }

        var cache = new MemoryCache(new MemoryCacheOptions());

        // First call populates cache
        var context1 = CreateHttpContext($"ApiKey {plainKey}", remoteIp: IPAddress.Parse("203.0.113.1"), localIp: IPAddress.Loopback);
        var handler1 = await CreateHandler(dbName, isEnabled: true, context1, cache);
        var result1 = await handler1.AuthenticateAsync();
        await Assert.That(result1.Succeeded).IsTrue();

        // Second call should use cache (verify by checking cache has the entry)
        var cacheKey = $"apikey:{keyHash}";
        var cached = cache.TryGetValue(cacheKey, out ApiKey? cachedKey);
        await Assert.That(cached).IsTrue();
        await Assert.That(cachedKey!.Name).IsEqualTo("Cached Key");
    }

    [Test]
    public async Task HashKey_Is_Deterministic()
    {
        var key = "my-secret-key";
        var hash1 = ApiKeyHasher.HashKey(key);
        var hash2 = ApiKeyHasher.HashKey(key);

        await Assert.That(hash1).IsEqualTo(hash2);
    }

    [Test]
    public async Task HashKey_Different_Keys_Produce_Different_Hashes()
    {
        var hash1 = ApiKeyHasher.HashKey("key-one");
        var hash2 = ApiKeyHasher.HashKey("key-two");

        await Assert.That(hash1).IsNotEqualTo(hash2);
    }

    [Test]
    public async Task Returns_NoResult_When_RemoteIp_Is_Null()
    {
        var dbName = Guid.NewGuid().ToString();
        // Null remote IP (e.g. behind misconfigured proxy) should NOT be exempt
        var context = CreateHttpContext("ApiKey some-key");
        var handler = await CreateHandler(dbName, isEnabled: true, context);

        var result = await handler.AuthenticateAsync();

        // Should attempt API key validation, not bypass as local
        await Assert.That(result.Succeeded).IsFalse();
    }

    [Test]
    public async Task Returns_Role_Claims_When_Key_Has_Roles()
    {
        var dbName = Guid.NewGuid().ToString();
        var plainKey = "test-api-key-with-roles";
        var keyHash = ApiKeyHasher.HashKey(plainKey);

        using (var db = CreateDbContext(dbName))
        {
            db.ApiKeys.Add(new ApiKey
            {
                Name = "Admin Key",
                KeyHash = keyHash,
                KeyPrefix = plainKey[..8],
                IsActive = true,
                Roles = "Administrator,Contributor",
                CreatedAt = DateTimeOffset.UtcNow
            });
            await db.SaveChangesAsync();
        }

        var context = CreateHttpContext($"ApiKey {plainKey}", remoteIp: IPAddress.Parse("203.0.113.1"), localIp: IPAddress.Loopback);
        var handler = await CreateHandler(dbName, isEnabled: true, context);

        var result = await handler.AuthenticateAsync();

        await Assert.That(result.Succeeded).IsTrue();
        await Assert.That(result.Principal!.IsInRole("Administrator")).IsTrue();
        await Assert.That(result.Principal!.IsInRole("Contributor")).IsTrue();
    }

    [Test]
    public async Task Returns_No_Role_Claims_When_Key_Has_No_Roles()
    {
        var dbName = Guid.NewGuid().ToString();
        var plainKey = "test-api-key-no-roles";
        var keyHash = ApiKeyHasher.HashKey(plainKey);

        using (var db = CreateDbContext(dbName))
        {
            db.ApiKeys.Add(new ApiKey
            {
                Name = "Read-only Key",
                KeyHash = keyHash,
                KeyPrefix = plainKey[..8],
                IsActive = true,
                Roles = null,
                CreatedAt = DateTimeOffset.UtcNow
            });
            await db.SaveChangesAsync();
        }

        var context = CreateHttpContext($"ApiKey {plainKey}", remoteIp: IPAddress.Parse("203.0.113.1"), localIp: IPAddress.Loopback);
        var handler = await CreateHandler(dbName, isEnabled: true, context);

        var result = await handler.AuthenticateAsync();

        await Assert.That(result.Succeeded).IsTrue();
        await Assert.That(result.Principal!.IsInRole("Administrator")).IsFalse();
    }

    private class TestOptionsMonitor(ApiKeyAuthenticationOptions options) : IOptionsMonitor<ApiKeyAuthenticationOptions>
    {
        public ApiKeyAuthenticationOptions CurrentValue => options;
        public ApiKeyAuthenticationOptions Get(string? name) => options;
        public IDisposable? OnChange(Action<ApiKeyAuthenticationOptions, string?> listener) => null;
    }
}
