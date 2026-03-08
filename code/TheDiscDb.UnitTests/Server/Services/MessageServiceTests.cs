using Microsoft.EntityFrameworkCore;
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

    [Test]
    public async Task SendAdminMessageAsync_CreatesAdminMessage()
    {
        var dbName = Guid.NewGuid().ToString();
        var factory = new TestDbContextFactory(dbName);
        var service = new MessageService(factory);

        var result = await service.SendAdminMessageAsync(42, "admin-1", "user-1", "Please fix the disc titles");

        await Assert.That(result.ContributionId).IsEqualTo(42);
        await Assert.That(result.FromUserId).IsEqualTo("admin-1");
        await Assert.That(result.ToUserId).IsEqualTo("user-1");
        await Assert.That(result.Message).IsEqualTo("Please fix the disc titles");
        await Assert.That(result.Type).IsEqualTo(UserMessageType.AdminMessage);
        await Assert.That(result.IsRead).IsEqualTo(false);

        // Verify persisted
        using var db = CreateDbContext(dbName);
        var messages = await db.UserMessages.ToListAsync();
        await Assert.That(messages).HasCount().EqualTo(1);
    }

    [Test]
    public async Task SendUserMessageAsync_CreatesUserMessage()
    {
        var dbName = Guid.NewGuid().ToString();
        var factory = new TestDbContextFactory(dbName);
        var service = new MessageService(factory);

        var result = await service.SendUserMessageAsync(42, "user-1", "I've updated the disc list");

        await Assert.That(result.ContributionId).IsEqualTo(42);
        await Assert.That(result.FromUserId).IsEqualTo("user-1");
        await Assert.That(result.Message).IsEqualTo("I've updated the disc list");
        await Assert.That(result.Type).IsEqualTo(UserMessageType.UserMessage);
        await Assert.That(result.IsRead).IsEqualTo(false);
    }

    [Test]
    public async Task SendUserMessageAsync_SetsToUserId_ToLastAdmin()
    {
        var dbName = Guid.NewGuid().ToString();
        var factory = new TestDbContextFactory(dbName);

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

        var service = new MessageService(factory);
        var result = await service.SendUserMessageAsync(42, "user-1", "Thanks for the feedback");

        await Assert.That(result.ToUserId).IsEqualTo("admin-latest");
    }

    [Test]
    public async Task SendUserMessageAsync_NoAdminMessages_SetsEmptyToUserId()
    {
        var dbName = Guid.NewGuid().ToString();
        var factory = new TestDbContextFactory(dbName);
        var service = new MessageService(factory);

        var result = await service.SendUserMessageAsync(42, "user-1", "Hello, anyone there?");

        await Assert.That(result.ToUserId).IsEqualTo(string.Empty);
    }

    [Test]
    public async Task SendAdminMessageAsync_SetsTimestampCloseToNow()
    {
        var dbName = Guid.NewGuid().ToString();
        var factory = new TestDbContextFactory(dbName);
        var service = new MessageService(factory);

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
        var factory = new TestDbContextFactory(dbName);

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

        var service = new MessageService(factory);
        var result = await service.SendUserMessageAsync(42, "user-1", "My message");

        // Should not pick up admin from contribution 99
        await Assert.That(result.ToUserId).IsEqualTo(string.Empty);
    }
}
