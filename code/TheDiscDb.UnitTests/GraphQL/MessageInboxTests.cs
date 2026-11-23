using System.Security.Claims;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.AspNetCore.Components.Authorization;
using Sqids;
using TheDiscDb.Data.Import;
using TheDiscDb.GraphQL.Contribute;
using TheDiscDb.GraphQL.Contribute.Exceptions;
using TheDiscDb.GraphQL.Contribute.Mutations;
using TheDiscDb.Services;
using TheDiscDb.Services.Server;
using TheDiscDb.Web.Data;

namespace TheDiscDb.UnitTests.GraphQL;

public class MessageInboxTests
{
    private static readonly IdEncoder Encoder = new(new SqidsEncoder<int>());

    private static ClaimsPrincipal User(string? id, bool admin = false, bool authenticated = true)
    {
        var claims = new List<Claim>();
        if (id != null)
            claims.Add(new Claim(ClaimTypes.NameIdentifier, id));
        if (admin)
            claims.Add(new Claim(ClaimTypes.Role, DefaultRoles.Administrator));
        return new ClaimsPrincipal(new ClaimsIdentity(claims, authenticated ? "test" : null));
    }

    [Test]
    [Arguments("admin-a")]
    [Arguments("admin-b")]
    public async Task SharedInbox_IncludesLegacyRecipientsAndOtherAdminReplies(string admin)
    {
        await using var fixture = await Fixture.Create();
        await using var db = fixture.Factory.CreateDbContext();
        var query = new ContributionQuery(Encoder);
        var user = User(admin, true);
        var threads = await query.GetMessageThreads(db, user);

        await Assert.That(threads.Count).IsEqualTo(2);
        var contribution = threads.Single(t => !t.IsBoxset);
        var boxset = threads.Single(t => t.IsBoxset);
        await Assert.That(contribution.ContributionId).IsEqualTo(boxset.ContributionId);
        await Assert.That(contribution.UnreadCount).IsEqualTo(2);
        await Assert.That(contribution.TotalCount).IsEqualTo(3);
        await Assert.That(contribution.LastMessagePreview).IsEqualTo("Admin reply");
        await Assert.That(boxset.UnreadCount).IsEqualTo(1);
        await Assert.That(await query.HasUnreadMessages(db, user)).IsTrue();
        await Assert.That(await query.GetMyMessages(db, user).CountAsync()).IsEqualTo(5);
        await Assert.That(await query.GetContributionChat(db, Encoder.Encode(fixture.ContributionId), user).CountAsync()).IsEqualTo(3);
        await Assert.That(await query.GetBoxsetChat(db, Encoder.Encode(fixture.BoxsetId), user).CountAsync()).IsEqualTo(2);
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task Read_OnlyAcknowledgesDisplayedIncomingIdsInTypedThread(bool boxset)
    {
        await using var fixture = await Fixture.Create();
        var displayed = boxset ? fixture.Messages[3] : fixture.Messages[0];
        var foreign = boxset ? fixture.Messages[0] : fixture.Messages[3];
        var arrival = new UserMessage
        {
            ContributionId = boxset ? null : fixture.ContributionId,
            BoxsetId = boxset ? fixture.BoxsetId : null,
            FromUserId = "owner",
            Message = "Concurrent arrival",
            CreatedAt = DateTimeOffset.UtcNow,
            Type = UserMessageType.UserMessage
        };
        await using (var seed = fixture.Factory.CreateDbContext())
        {
            seed.UserMessages.Add(arrival);
            await seed.SaveChangesAsync();
        }

        var service = fixture.Service;
        var ids = new[] { displayed.Id, displayed.Id, foreign.Id };
        if (boxset)
            await service.MarkBoxsetMessagesAsReadAsync(fixture.BoxsetId, User("admin-a", true), ids);
        else
            await service.MarkContributionMessagesAsReadAsync(fixture.ContributionId, User("admin-a", true), ids);

        await using var db = fixture.Factory.CreateDbContext();
        await Assert.That((await db.UserMessages.FindAsync(displayed.Id))!.IsRead).IsTrue();
        await Assert.That((await db.UserMessages.FindAsync(foreign.Id))!.IsRead).IsFalse();
        await Assert.That((await db.UserMessages.FindAsync(arrival.Id))!.IsRead).IsFalse();
        await Assert.That((await db.UserMessages.FindAsync(fixture.Messages[2].Id))!.IsRead).IsFalse();
        await Assert.That((await db.UserMessages.FindAsync(fixture.Messages[4].Id))!.IsRead).IsFalse();
        await Assert.That(await new ContributionQuery(Encoder).HasUnreadMessages(db, User("admin-b", true))).IsTrue();
        await Assert.That(fixture.Notifications.Calls).IsEqualTo(0);
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task OwnerRead_DoesNotAcknowledgeOutgoingTeamMessages(bool boxset)
    {
        await using var fixture = await Fixture.Create();
        var ids = fixture.Messages.Select(m => m.Id).ToArray();
        if (boxset)
            await fixture.Service.MarkBoxsetMessagesAsReadAsync(fixture.BoxsetId, User("owner"), ids);
        else
            await fixture.Service.MarkContributionMessagesAsReadAsync(fixture.ContributionId, User("owner"), ids);

        await using var db = fixture.Factory.CreateDbContext();
        await Assert.That((await db.UserMessages.FindAsync(boxset ? fixture.Messages[4].Id : fixture.Messages[2].Id))!.IsRead).IsTrue();
        await Assert.That((await db.UserMessages.FindAsync(boxset ? fixture.Messages[3].Id : fixture.Messages[0].Id))!.IsRead).IsFalse();
        var threads = await new ContributionQuery(Encoder).GetMessageThreads(db, User("admin-b", true));
        await Assert.That(threads.Single(t => t.IsBoxset == boxset).UnreadCount).IsEqualTo(boxset ? 1 : 2);
    }

    [Test]
    public async Task SharedRead_ClearsTeamForBothAdminsButPreservesOwnerUnreadReplies()
    {
        await using var fixture = await Fixture.Create();
        var ids = fixture.Messages.Select(m => m.Id).ToArray();
        await fixture.Service.MarkContributionMessagesAsReadAsync(fixture.ContributionId, User("admin-a", true), ids);
        await fixture.Service.MarkBoxsetMessagesAsReadAsync(fixture.BoxsetId, User("admin-a", true), ids);
        await fixture.Service.MarkContributionMessagesAsReadAsync(fixture.ContributionId, User("admin-b", true), ids);

        await using var db = fixture.Factory.CreateDbContext();
        var query = new ContributionQuery(Encoder);
        await Assert.That(await query.HasUnreadMessages(db, User("admin-a", true))).IsFalse();
        await Assert.That(await query.HasUnreadMessages(db, User("admin-b", true))).IsFalse();
        await Assert.That(await query.HasUnreadMessages(db, User("owner"))).IsTrue();
        await Assert.That((await query.GetMessageThreads(db, User("admin-b", true))).Count).IsEqualTo(2);
        await Assert.That(fixture.Notifications.Calls).IsEqualTo(0);
    }

    [Test]
    public async Task AdministratorOwner_CanReadPersonalAdminReplyWithoutClearingOtherOwnersReply()
    {
        await using var fixture = await Fixture.Create();
        int id;
        await using (var seed = fixture.Factory.CreateDbContext())
        {
            var contribution = new UserContribution { UserId = "admin-a", Created = DateTimeOffset.UtcNow };
            seed.UserContributions.Add(contribution);
            var message = new UserMessage
            {
                Contribution = contribution,
                FromUserId = "admin-b",
                ToUserId = "admin-a",
                Message = "Personal admin reply",
                CreatedAt = DateTimeOffset.UtcNow,
                Type = UserMessageType.AdminMessage
            };
            seed.UserMessages.Add(message);
            await seed.SaveChangesAsync();
            id = message.Id;
            await fixture.Service.MarkContributionMessagesAsReadAsync(contribution.Id, User("admin-a", true), [id, fixture.Messages[2].Id]);
        }

        await using var db = fixture.Factory.CreateDbContext();
        await Assert.That((await db.UserMessages.FindAsync(id))!.IsRead).IsTrue();
        await Assert.That((await db.UserMessages.FindAsync(fixture.Messages[2].Id))!.IsRead).IsFalse();
    }

    [Test]
    [Arguments("outsider", false, true)]
    [Arguments("admin-a", false, true)]
    [Arguments("owner", false, false)]
    [Arguments(null, true, true)]
    public async Task NonOwnersAndInvalidPrincipals_CannotAccessOrAcknowledgeThreads(string? id, bool admin, bool authenticated)
    {
        await using var fixture = await Fixture.Create();
        await using var db = fixture.Factory.CreateDbContext();
        var user = User(id, admin, authenticated);
        var query = new ContributionQuery(Encoder);
        await Assert.That(await query.HasUnreadMessages(db, user)).IsFalse();
        await Assert.That((await query.GetMessageThreads(db, user)).Count).IsEqualTo(0);
        await Assert.That(await query.GetMyMessages(db, user).CountAsync()).IsEqualTo(0);
        await Assert.That(await query.GetContributionChat(db, Encoder.Encode(fixture.ContributionId), user).CountAsync()).IsEqualTo(0);
        await Assert.That(await query.GetBoxsetChat(db, Encoder.Encode(fixture.BoxsetId), user).CountAsync()).IsEqualTo(0);

        if (!authenticated || id == null)
        {
            await Assert.That(() => fixture.Service.MarkContributionMessagesAsReadAsync(fixture.ContributionId, user, [fixture.Messages[0].Id])).Throws<AuthenticationException>();
            await Assert.That(() => fixture.Service.MarkBoxsetMessagesAsReadAsync(fixture.BoxsetId, user, [fixture.Messages[3].Id])).Throws<AuthenticationException>();
        }
        else
        {
            await Assert.That(() => fixture.Service.MarkContributionMessagesAsReadAsync(fixture.ContributionId, user, [fixture.Messages[0].Id])).Throws<InvalidOwnershipException>();
            await Assert.That(() => fixture.Service.MarkBoxsetMessagesAsReadAsync(fixture.BoxsetId, user, [fixture.Messages[3].Id])).Throws<InvalidOwnershipException>();
        }
    }

    [Test]
    public async Task EmptyAcknowledgementsAndMissingThreads_DoNotMarkMessagesRead()
    {
        await using var fixture = await Fixture.Create();
        var admin = User("admin-a", true);
        await fixture.Service.MarkContributionMessagesAsReadAsync(fixture.ContributionId, admin, null);
        await fixture.Service.MarkBoxsetMessagesAsReadAsync(fixture.BoxsetId, admin, []);
        await Assert.That(() => fixture.Service.MarkContributionMessagesAsReadAsync(int.MaxValue, admin, [fixture.Messages[0].Id])).Throws<ContributionNotFoundException>();
        await Assert.That(() => fixture.Service.MarkBoxsetMessagesAsReadAsync(int.MaxValue, admin, [fixture.Messages[3].Id])).Throws<BoxsetNotFoundException>();
        await using var db = fixture.Factory.CreateDbContext();
        await Assert.That(await db.UserMessages.CountAsync(m => m.IsRead)).IsEqualTo(0);
    }

    [Test]
    public async Task BoxsetRouting_RemainsSharedAfterDifferentAdminReplies()
    {
        await using var fixture = await Fixture.Create();
        await fixture.Service.SendAdminBoxsetMessageAsync(fixture.BoxsetId, "admin-b", "owner", "Review");
        var message = await fixture.Service.SendUserBoxsetMessageAsync(fixture.BoxsetId, "owner", "Reply");
        await using var db = fixture.Factory.CreateDbContext();
        await Assert.That((await new ContributionQuery(Encoder).GetMessageThreads(db, User("admin-a", true))).Single(t => t.IsBoxset).UnreadCount).IsEqualTo(2);
        await Assert.That(message.ToUserId).IsEqualTo(string.Empty);
        await Assert.That(fixture.Notifications.Calls).IsEqualTo(0);
    }

    [Test]
    public async Task ReadMutations_UseBoundedIdsAndReturnTypedAuthorizationErrors()
    {
        await using var fixture = await Fixture.Create();
        var store = new UnusedAssetStore();
        var admin = new ContributionMutations(Encoder, new TestPrincipal(User("admin-a", true)), store, store);
        await admin.MarkMessagesAsRead(Encoder.Encode(fixture.ContributionId), null, fixture.Service, CancellationToken.None);
        await admin.MarkBoxsetMessagesAsRead(Encoder.Encode(fixture.BoxsetId), [fixture.Messages[3].Id], fixture.Service, CancellationToken.None);
        await Assert.That(() => admin.MarkMessagesAsRead("", [], fixture.Service, CancellationToken.None)).Throws<InvalidIdException>();
        var outsider = new ContributionMutations(Encoder, new TestPrincipal(User("outsider")), store, store);
        await Assert.That(() => outsider.MarkMessagesAsRead(Encoder.Encode(fixture.ContributionId), [fixture.Messages[0].Id], fixture.Service, CancellationToken.None)).Throws<InvalidOwnershipException>();
        await using var db = fixture.Factory.CreateDbContext();
        await Assert.That((await db.UserMessages.FindAsync(fixture.Messages[0].Id))!.IsRead).IsFalse();
        await Assert.That((await db.UserMessages.FindAsync(fixture.Messages[3].Id))!.IsRead).IsTrue();
    }

    [Test]
    public async Task ServerBadge_UsesAuthenticatedViewerInsteadOfServiceApiKey()
    {
        await using var fixture = await Fixture.Create();
        await fixture.Service.MarkContributionMessagesAsReadAsync(fixture.ContributionId, User("admin-a", true), fixture.Messages.Select(m => m.Id).ToArray());
        await fixture.Service.MarkBoxsetMessagesAsReadAsync(fixture.BoxsetId, User("admin-a", true), fixture.Messages.Select(m => m.Id).ToArray());
        var owner = new ServerUnreadMessagesService(fixture.Factory, new Authentication(User("owner")));
        var admin = new ServerUnreadMessagesService(fixture.Factory, new Authentication(User("admin-b", true)));
        await Assert.That(await owner.HasUnreadMessagesAsync()).IsTrue();
        await Assert.That(await admin.HasUnreadMessagesAsync()).IsFalse();
    }

    private sealed class TestPrincipal(ClaimsPrincipal user) : IPrincipalProvider
    {
        public ClaimsPrincipal? Principal => user;
    }

    private sealed class Authentication(ClaimsPrincipal user) : AuthenticationStateProvider
    {
        public override Task<AuthenticationState> GetAuthenticationStateAsync() => Task.FromResult(new AuthenticationState(user));
    }

    private sealed class UnusedAssetStore : IStaticAssetStore
    {
        public string ContainerName { get; set; } = "unused";
        public Task<string> Save(Stream stream, string path, string type, CancellationToken token = default) => throw new NotSupportedException();
        public Task<string> Save(string file, string path, string type, CancellationToken token = default) => throw new NotSupportedException();
        public Task<bool> Exists(string path, CancellationToken token = default) => throw new NotSupportedException();
        public Task<BinaryData> Download(string path, CancellationToken token = default) => throw new NotSupportedException();
        public Task Delete(string path, CancellationToken token = default) => throw new NotSupportedException();
    }

    private sealed class Fixture : IAsyncDisposable
    {
        public TestFactory Factory { get; } = new();
        public Notifications Notifications { get; } = new();
        public MessageService Service => new(this.Factory, this.Notifications, null!, NullLogger<MessageService>.Instance);
        public List<UserMessage> Messages { get; } = [];
        public int ContributionId { get; private set; }
        public int BoxsetId { get; private set; }

        public static async Task<Fixture> Create()
        {
            var fixture = new Fixture();
            await using var db = fixture.Factory.CreateDbContext();
            await db.Database.EnsureCreatedAsync();
            var contribution = new UserContribution { UserId = "owner", Title = "Test movie", ReleaseTitle = "Test release", Created = DateTimeOffset.UtcNow };
            var boxset = new UserContributionBoxset { UserId = "owner", Title = "Test boxset", Created = DateTimeOffset.UtcNow };
            db.UserContributions.Add(contribution);
            db.UserContributionBoxsets.Add(boxset);
            await db.SaveChangesAsync();
            fixture.ContributionId = contribution.Id;
            fixture.BoxsetId = boxset.Id;
            var now = DateTimeOffset.UtcNow;
            fixture.Messages.AddRange([
                new() { ContributionId = contribution.Id, FromUserId = "owner", ToUserId = "", Message = "First user message", CreatedAt = now.AddMinutes(-3), Type = UserMessageType.UserMessage },
                new() { ContributionId = contribution.Id, FromUserId = "owner", ToUserId = "admin-a", Message = "Legacy addressed reply", CreatedAt = now.AddMinutes(-2), Type = UserMessageType.UserMessage },
                new() { ContributionId = contribution.Id, FromUserId = "admin-b", ToUserId = "owner", Message = "Admin reply", CreatedAt = now, Type = UserMessageType.AdminMessage },
                new() { BoxsetId = boxset.Id, FromUserId = "owner", ToUserId = "", Message = "Boxset user message", CreatedAt = now.AddMinutes(-1), Type = UserMessageType.UserMessage },
                new() { BoxsetId = boxset.Id, FromUserId = "admin-b", ToUserId = "owner", Message = "Boxset admin reply", CreatedAt = now, Type = UserMessageType.AdminMessage }
            ]);
            db.UserMessages.AddRange(fixture.Messages);
            db.UserMessages.Add(new UserMessage { FromUserId = "owner", ToUserId = "admin-a", Message = "Deleted thread", CreatedAt = now, Type = UserMessageType.UserMessage });
            await db.SaveChangesAsync();
            return fixture;
        }

        public async ValueTask DisposeAsync()
        {
            await using var db = this.Factory.CreateDbContext();
            await db.Database.EnsureDeletedAsync();
        }
    }

    private sealed class TestFactory : IDbContextFactory<SqlServerDataContext>
    {
        private readonly DbContextOptions<SqlServerDataContext> options;

        public TestFactory()
        {
            var builder = new DbContextOptionsBuilder<SqlServerDataContext>();
            var sql = Environment.GetEnvironmentVariable("THEDISCDB_MESSAGE_TEST_SQL");
            if (string.IsNullOrEmpty(sql))
            {
                builder.UseInMemoryDatabase(Guid.NewGuid().ToString());
            }
            else
            {
                var connection = new SqlConnectionStringBuilder(sql);
                var source = connection.DataSource.Replace("tcp:", "", StringComparison.OrdinalIgnoreCase);
                if (!source.StartsWith("localhost,", StringComparison.OrdinalIgnoreCase) &&
                    !source.StartsWith("127.0.0.1,", StringComparison.OrdinalIgnoreCase))
                    throw new InvalidOperationException("Message SQL tests require a dedicated localhost container.");
                // Every case owns its database; never clear the configured application database.
                connection.InitialCatalog = $"message_validation_{Guid.NewGuid():N}";
                builder.UseSqlServer(connection.ConnectionString);
            }
            this.options = builder.Options;
        }

        public SqlServerDataContext CreateDbContext() => new(this.options);
    }

    private sealed class Notifications : IContributionNotificationService
    {
        public int Calls { get; private set; }
        private Task Record() { this.Calls++; return Task.CompletedTask; }
        public Task NotifyContributionCreatedAsync(UserContribution c, string? email, string? name, CancellationToken token = default) => this.Record();
        public Task NotifyContributionImportedAsync(UserContribution c, string? email, CancellationToken token = default) => this.Record();
        public Task NotifyMessageFromUserAsync(UserContribution c, string message, string? name, string? email, CancellationToken token = default) => this.Record();
        public Task NotifyMessageFromAdminAsync(UserContribution c, string message, string? email, CancellationToken token = default) => this.Record();
    }
}