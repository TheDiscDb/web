using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using TheDiscDb.Services;
using TheDiscDb.Web.Data;

namespace TheDiscDb.UnitTests.Server.Services;

public class MessageServiceTests
{
    private static SqlServerDataContext CreateDbContext(string? dbName = null)
    {
        var options = new DbContextOptionsBuilder<SqlServerDataContext>()
            .UseInMemoryDatabase(databaseName: dbName ?? Guid.NewGuid().ToString())
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

    /// <summary>
    /// Records notification calls so tests can verify count and arguments.
    /// </summary>
    private class RecordingNotificationService : IContributionNotificationService
    {
        public List<(string Method, int ContributionId)> Calls { get; } = [];

        public Task NotifyContributionCreatedAsync(UserContribution contribution, string? userEmail, string? userName)
        {
            Calls.Add(("ContributionCreated", contribution.Id));
            return Task.CompletedTask;
        }

        public Task NotifyContributionImportedAsync(UserContribution contribution, string? userEmail)
        {
            Calls.Add(("ContributionImported", contribution.Id));
            return Task.CompletedTask;
        }

        public Task NotifyMessageFromUserAsync(UserContribution contribution, string message, string? userName, string? userEmail)
        {
            Calls.Add(("MessageFromUser", contribution.Id));
            return Task.CompletedTask;
        }

        public Task NotifyMessageFromAdminAsync(UserContribution contribution, string message, string? userEmail)
        {
            Calls.Add(("MessageFromAdmin", contribution.Id));
            return Task.CompletedTask;
        }
    }

    private static MessageService CreateService(string dbName, out RecordingNotificationService notifications)
    {
        var factory = new TestDbContextFactory(dbName);
        notifications = new RecordingNotificationService();
        var userStore = new InMemoryUserStore();
        var userManager = new UserManager<TheDiscDbUser>(userStore, null!, null!, null!, null!, null!, null!, null!, null!);
        var logger = NullLogger<MessageService>.Instance;
        return new MessageService(factory, notifications, userManager, logger);
    }

    private static MessageService CreateService(string dbName)
    {
        return CreateService(dbName, out _);
    }

    /// <summary>
    /// Minimal IUserStore for UserManager — tests don't actually look up users.
    /// </summary>
    private class InMemoryUserStore : IUserStore<TheDiscDbUser>
    {
        public Task<string> GetUserIdAsync(TheDiscDbUser user, CancellationToken cancellationToken) => Task.FromResult(user.Id);
        public Task<string?> GetUserNameAsync(TheDiscDbUser user, CancellationToken cancellationToken) => Task.FromResult(user.UserName);
        public Task SetUserNameAsync(TheDiscDbUser user, string? userName, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task<string?> GetNormalizedUserNameAsync(TheDiscDbUser user, CancellationToken cancellationToken) => Task.FromResult(user.NormalizedUserName);
        public Task SetNormalizedUserNameAsync(TheDiscDbUser user, string? normalizedName, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task<IdentityResult> CreateAsync(TheDiscDbUser user, CancellationToken cancellationToken) => Task.FromResult(IdentityResult.Success);
        public Task<IdentityResult> UpdateAsync(TheDiscDbUser user, CancellationToken cancellationToken) => Task.FromResult(IdentityResult.Success);
        public Task<IdentityResult> DeleteAsync(TheDiscDbUser user, CancellationToken cancellationToken) => Task.FromResult(IdentityResult.Success);
        public Task<TheDiscDbUser?> FindByIdAsync(string userId, CancellationToken cancellationToken) => Task.FromResult<TheDiscDbUser?>(null);
        public Task<TheDiscDbUser?> FindByNameAsync(string normalizedUserName, CancellationToken cancellationToken) => Task.FromResult<TheDiscDbUser?>(null);
        public void Dispose() { }
    }

    private static void SeedContribution(string dbName, int id = 42)
    {
        using var db = CreateDbContext(dbName);
        db.UserContributions.Add(new UserContribution
        {
            Id = id,
            Title = "Test Movie",
            Year = "2025",
            ReleaseTitle = "2025 4K",
            MediaType = "movie",
            UserId = "user-1",
            Asin = "B000000000",
            Upc = "123456789012",
            Status = UserContributionStatus.Pending,
            Created = DateTimeOffset.UtcNow
        });
        db.SaveChanges();
    }

    [Test]
    public async Task SendAdminMessageAsync_CreatesAdminMessage()
    {
        var dbName = Guid.NewGuid().ToString();
        var service = CreateService(dbName);

        var result = await service.SendAdminMessageAsync(42, "admin-1", "user-1", "Please fix the disc titles");

        await Assert.That(result.ContributionId).IsEqualTo(42);
        await Assert.That(result.FromUserId).IsEqualTo("admin-1");
        await Assert.That(result.ToUserId).IsEqualTo("user-1");
        await Assert.That(result.Message).IsEqualTo("Please fix the disc titles");
        await Assert.That(result.Type).IsEqualTo(UserMessageType.AdminMessage);
        await Assert.That(result.IsRead).IsFalse();

        // Verify persisted
        using var db = CreateDbContext(dbName);
        var messages = await db.UserMessages.ToListAsync();
        await Assert.That(messages).Count().IsEqualTo(1);
    }

    [Test]
    public async Task SendUserMessageAsync_CreatesUserMessage()
    {
        var dbName = Guid.NewGuid().ToString();
        var service = CreateService(dbName);

        var result = await service.SendUserMessageAsync(42, "user-1", "I've updated the disc list");

        await Assert.That(result.ContributionId).IsEqualTo(42);
        await Assert.That(result.FromUserId).IsEqualTo("user-1");
        await Assert.That(result.Message).IsEqualTo("I've updated the disc list");
        await Assert.That(result.Type).IsEqualTo(UserMessageType.UserMessage);
        await Assert.That(result.IsRead).IsFalse();
    }

    [Test]
    public async Task SendUserMessageAsync_SetsToUserId_ToLastAdmin()
    {
        var dbName = Guid.NewGuid().ToString();

        // Seed admin messages from two different admins
        using (var db = CreateDbContext(dbName))
        {
            db.UserMessages.Add(new UserMessage
            {
                ContributionId = 42,
                FromUserId = "admin-old",
                ToUserId = "user-1",
                Message = "First review",
                IsRead = true,
                CreatedAt = DateTimeOffset.UtcNow.AddHours(-2),
                Type = UserMessageType.AdminMessage
            });
            db.UserMessages.Add(new UserMessage
            {
                ContributionId = 42,
                FromUserId = "admin-latest",
                ToUserId = "user-1",
                Message = "Second review",
                IsRead = true,
                CreatedAt = DateTimeOffset.UtcNow.AddHours(-1),
                Type = UserMessageType.AdminMessage
            });
            await db.SaveChangesAsync();
        }

        var service = CreateService(dbName);
        var result = await service.SendUserMessageAsync(42, "user-1", "Thanks for the feedback");

        await Assert.That(result.ToUserId).IsEqualTo("admin-latest");
    }

    [Test]
    public async Task SendUserMessageAsync_NoAdminMessages_SetsEmptyToUserId()
    {
        var dbName = Guid.NewGuid().ToString();
        var service = CreateService(dbName);

        var result = await service.SendUserMessageAsync(42, "user-1", "Hello, anyone there?");

        await Assert.That(result.ToUserId).IsEqualTo(string.Empty);
    }

    [Test]
    public async Task SendAdminMessageAsync_SetsTimestampCloseToNow()
    {
        var dbName = Guid.NewGuid().ToString();
        var service = CreateService(dbName);

        var before = DateTimeOffset.UtcNow;
        var result = await service.SendAdminMessageAsync(1, "admin-1", "user-1", "test");
        var after = DateTimeOffset.UtcNow;

        await Assert.That(result.CreatedAt).IsGreaterThanOrEqualTo(before);
        await Assert.That(result.CreatedAt).IsLessThanOrEqualTo(after);
    }

    [Test]
    public async Task SendUserMessageAsync_IgnoresAdminMessagesFromOtherContributions()
    {
        var dbName = Guid.NewGuid().ToString();

        // Seed admin message on a DIFFERENT contribution
        using (var db = CreateDbContext(dbName))
        {
            db.UserMessages.Add(new UserMessage
            {
                ContributionId = 99,
                FromUserId = "admin-other",
                ToUserId = "user-1",
                Message = "Wrong contribution",
                IsRead = true,
                CreatedAt = DateTimeOffset.UtcNow,
                Type = UserMessageType.AdminMessage
            });
            await db.SaveChangesAsync();
        }

        var service = CreateService(dbName);
        var result = await service.SendUserMessageAsync(42, "user-1", "My message");

        // Should not pick up admin from contribution 99
        await Assert.That(result.ToUserId).IsEqualTo(string.Empty);
    }

    [Test]
    public async Task SendAdminMessageAsync_SendsExactlyOneNotification()
    {
        var dbName = Guid.NewGuid().ToString();
        SeedContribution(dbName);
        var service = CreateService(dbName, out var notifications);

        await service.SendAdminMessageAsync(42, "admin-1", "user-1", "Changes needed");

        await Assert.That(notifications.Calls).Count().IsEqualTo(1);
        await Assert.That(notifications.Calls[0].Method).IsEqualTo("MessageFromAdmin");
        await Assert.That(notifications.Calls[0].ContributionId).IsEqualTo(42);
    }

    [Test]
    public async Task SendUserMessageAsync_SendsExactlyOneNotification()
    {
        var dbName = Guid.NewGuid().ToString();
        SeedContribution(dbName);
        var service = CreateService(dbName, out var notifications);

        await service.SendUserMessageAsync(42, "user-1", "I fixed everything");

        await Assert.That(notifications.Calls).Count().IsEqualTo(1);
        await Assert.That(notifications.Calls[0].Method).IsEqualTo("MessageFromUser");
        await Assert.That(notifications.Calls[0].ContributionId).IsEqualTo(42);
    }

    [Test]
    public async Task SendAdminMessageAsync_NoContributionInDb_SkipsNotification()
    {
        var dbName = Guid.NewGuid().ToString();
        // Don't seed contribution — ID 999 won't be found
        var service = CreateService(dbName, out var notifications);

        await service.SendAdminMessageAsync(999, "admin-1", "user-1", "test");

        await Assert.That(notifications.Calls).Count().IsEqualTo(0);
    }
}
