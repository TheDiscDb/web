namespace TheDiscDb.UnitTests.Services.EditSuggestions;

using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using TheDiscDb.Services;
using TheDiscDb.Services.EditSuggestions;
using TheDiscDb.Web.Data;

public class EditSuggestionMessagingTests
{
    private static SqlServerDataContext CreateDb(string dbName)
    {
        var options = new DbContextOptionsBuilder<SqlServerDataContext>()
            .UseInMemoryDatabase(dbName)
            .Options;
        return new SqlServerDataContext(options);
    }

    private static async Task<int> SeedSuggestionAsync(string dbName, string ownerId = "user-1")
    {
        await using var db = CreateDb(dbName);
        var suggestion = new EditSuggestion
        {
            UserId = ownerId,
            Created = DateTimeOffset.UtcNow,
            Status = EditSuggestionStatus.Pending,
            TargetEntityType = "Release",
            TargetEntityKey = "movie/release",
        };
        db.EditSuggestions.Add(suggestion);
        await db.SaveChangesAsync();
        return suggestion.Id;
    }

    private static MessageService CreateService(
        string dbName,
        out RecordingEditSuggestionNotifications notifications)
    {
        notifications = new RecordingEditSuggestionNotifications();
        var users = new TestUserStore(
        [
            new TheDiscDbUser { Id = "user-1", UserName = "Test User", Email = "user@example.com" },
            new TheDiscDbUser { Id = "admin-1", UserName = "Admin", Email = "admin@example.com" },
            new TheDiscDbUser { Id = "admin-2", UserName = "Other Admin", Email = "admin2@example.com" },
        ]);
        var userManager = new UserManager<TheDiscDbUser>(
            users, null!, null!, null!, null!, null!, null!, null!, null!);

        return new MessageService(
            new TestDbContextFactory(dbName),
            new NullContributionNotifications(),
            notifications,
            userManager,
            NullLogger<MessageService>.Instance);
    }

    [Test]
    public async Task SendAdminEditSuggestionMessageAsync_PersistsUnifiedMessageAndNotifiesOwner()
    {
        var dbName = Guid.NewGuid().ToString();
        var suggestionId = await SeedSuggestionAsync(dbName);
        var service = CreateService(dbName, out var notifications);

        var message = await service.SendAdminEditSuggestionMessageAsync(
            suggestionId, "admin-1", "Need more information");

        await Assert.That(message.EditSuggestionId).IsEqualTo(suggestionId);
        await Assert.That(message.ToUserId).IsEqualTo("user-1");
        await Assert.That(message.Type).IsEqualTo(UserMessageType.AdminMessage);
        await Assert.That(notifications.AdminMessages).Count().IsEqualTo(1);

        await using var db = CreateDb(dbName);
        await Assert.That(await db.UserMessages.CountAsync()).IsEqualTo(1);
    }

    [Test]
    public async Task SendUserEditSuggestionMessageAsync_RequiresOwnershipAndRoutesToLatestAdmin()
    {
        var dbName = Guid.NewGuid().ToString();
        var suggestionId = await SeedSuggestionAsync(dbName);
        var service = CreateService(dbName, out _);
        await service.SendAdminEditSuggestionMessageAsync(
            suggestionId, "admin-1", "First", sendNotification: false);
        await service.SendAdminEditSuggestionMessageAsync(
            suggestionId, "admin-2", "Second", sendNotification: false);

        var reply = await service.SendUserEditSuggestionMessageAsync(
            suggestionId, "user-1", "Here is the clarification");

        await Assert.That(reply.ToUserId).IsEqualTo("admin-2");
        await Assert.That(reply.Type).IsEqualTo(UserMessageType.UserMessage);
        await Assert.That(async () =>
            await service.SendUserEditSuggestionMessageAsync(suggestionId, "intruder", "No"))
            .Throws<UnauthorizedAccessException>();
    }

    [Test]
    public async Task SendUserEditSuggestionMessageAsync_RequiresExistingAdminParticipant()
    {
        var dbName = Guid.NewGuid().ToString();
        var suggestionId = await SeedSuggestionAsync(dbName);
        var service = CreateService(dbName, out _);

        await Assert.That(async () =>
            await service.SendUserEditSuggestionMessageAsync(
                suggestionId, "user-1", "Can someone review this?"))
            .Throws<InvalidOperationException>();
    }

    [Test]
    public async Task EditSuggestionMessages_ValidateLengthAndCanBeMarkedRead()
    {
        var dbName = Guid.NewGuid().ToString();
        var suggestionId = await SeedSuggestionAsync(dbName);
        var service = CreateService(dbName, out _);

        await Assert.That(async () =>
            await service.SendAdminEditSuggestionMessageAsync(suggestionId, "admin-1", " "))
            .Throws<ArgumentException>();

        await service.SendAdminEditSuggestionMessageAsync(
            suggestionId, "admin-1", "Please review", sendNotification: false);
        await service.MarkEditSuggestionMessagesAsReadAsync(suggestionId, "user-1");

        await using var db = CreateDb(dbName);
        var message = await db.UserMessages.SingleAsync();
        await Assert.That(message.IsRead).IsTrue();
    }

    private sealed class TestDbContextFactory(string dbName) : IDbContextFactory<SqlServerDataContext>
    {
        public SqlServerDataContext CreateDbContext() => CreateDb(dbName);
    }

    private sealed class TestUserStore(IEnumerable<TheDiscDbUser> users) : IUserStore<TheDiscDbUser>
    {
        private readonly Dictionary<string, TheDiscDbUser> users = users.ToDictionary(user => user.Id);

        public Task<TheDiscDbUser?> FindByIdAsync(string userId, CancellationToken cancellationToken) =>
            Task.FromResult(users.GetValueOrDefault(userId));
        public Task<TheDiscDbUser?> FindByNameAsync(string normalizedUserName, CancellationToken cancellationToken) =>
            Task.FromResult(users.Values.FirstOrDefault(user => user.NormalizedUserName == normalizedUserName));
        public Task<string> GetUserIdAsync(TheDiscDbUser user, CancellationToken cancellationToken) => Task.FromResult(user.Id);
        public Task<string?> GetUserNameAsync(TheDiscDbUser user, CancellationToken cancellationToken) => Task.FromResult(user.UserName);
        public Task<string?> GetNormalizedUserNameAsync(TheDiscDbUser user, CancellationToken cancellationToken) => Task.FromResult(user.NormalizedUserName);
        public Task SetUserNameAsync(TheDiscDbUser user, string? userName, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task SetNormalizedUserNameAsync(TheDiscDbUser user, string? normalizedName, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task<IdentityResult> CreateAsync(TheDiscDbUser user, CancellationToken cancellationToken) => Task.FromResult(IdentityResult.Success);
        public Task<IdentityResult> UpdateAsync(TheDiscDbUser user, CancellationToken cancellationToken) => Task.FromResult(IdentityResult.Success);
        public Task<IdentityResult> DeleteAsync(TheDiscDbUser user, CancellationToken cancellationToken) => Task.FromResult(IdentityResult.Success);
        public void Dispose() { }
    }

    private sealed class RecordingEditSuggestionNotifications : IEditSuggestionNotificationService
    {
        public List<string> AdminMessages { get; } = [];
        public List<string> UserMessages { get; } = [];

        public Task NotifySuggestionSubmittedAsync(EditSuggestion suggestion, string? userEmail, string? userName, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task NotifySuggestionResolvedAsync(EditSuggestion suggestion, string? userEmail, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task NotifyMessageFromUserAsync(EditSuggestion suggestion, string message, string? userName, string? userEmail, CancellationToken cancellationToken = default)
        {
            UserMessages.Add(message);
            return Task.CompletedTask;
        }
        public Task NotifyMessageFromAdminAsync(EditSuggestion suggestion, string message, string? userEmail, CancellationToken cancellationToken = default)
        {
            AdminMessages.Add(message);
            return Task.CompletedTask;
        }
    }

    private sealed class NullContributionNotifications : IContributionNotificationService
    {
        public Task NotifyContributionCreatedAsync(UserContribution contribution, string? userEmail, string? userName, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task NotifyContributionImportedAsync(UserContribution contribution, string? userEmail, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task NotifyMessageFromUserAsync(UserContribution contribution, string message, string? userName, string? userEmail, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task NotifyMessageFromAdminAsync(UserContribution contribution, string message, string? userEmail, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }
}
