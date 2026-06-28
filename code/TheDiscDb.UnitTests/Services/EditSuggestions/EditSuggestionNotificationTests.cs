namespace TheDiscDb.UnitTests.Services.EditSuggestions;

using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using TheDiscDb.Data.Changes;
using TheDiscDb.Data.Changes.ReleaseFields;
using TheDiscDb.InputModels;
using TheDiscDb.Services.EditSuggestions;
using TheDiscDb.Web.Data;

public class EditSuggestionNotificationTests
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private static readonly CancellationToken CT = CancellationToken.None;

    // ---- Recording fakes -------------------------------------------------

    private sealed record SubmittedCall(int SuggestionId, string? Email, string? Name);
    private sealed record ResolvedCall(int SuggestionId, EditSuggestionStatus Status, string? Email);
    private sealed record MessageCall(int SuggestionId, bool FromAdmin, string Body, string? Email, string? Name);

    private sealed class RecordingNotificationService : IEditSuggestionNotificationService
    {
        public List<SubmittedCall> Submitted { get; } = [];
        public List<ResolvedCall> Resolved { get; } = [];
        public List<MessageCall> Messages { get; } = [];

        public Task NotifySuggestionSubmittedAsync(EditSuggestion suggestion, string? userEmail, string? userName, CancellationToken cancellationToken = default)
        {
            Submitted.Add(new SubmittedCall(suggestion.Id, userEmail, userName));
            return Task.CompletedTask;
        }

        public Task NotifySuggestionResolvedAsync(EditSuggestion suggestion, string? userEmail, CancellationToken cancellationToken = default)
        {
            Resolved.Add(new ResolvedCall(suggestion.Id, suggestion.Status, userEmail));
            return Task.CompletedTask;
        }

        public Task NotifyMessageFromUserAsync(EditSuggestion suggestion, string message, string? userName, string? userEmail, CancellationToken cancellationToken = default)
        {
            Messages.Add(new MessageCall(suggestion.Id, FromAdmin: false, message, userEmail, userName));
            return Task.CompletedTask;
        }

        public Task NotifyMessageFromAdminAsync(EditSuggestion suggestion, string message, string? userEmail, CancellationToken cancellationToken = default)
        {
            Messages.Add(new MessageCall(suggestion.Id, FromAdmin: true, message, userEmail, null));
            return Task.CompletedTask;
        }
    }

    private sealed class FakeRecipientResolver : IEditSuggestionRecipientResolver
    {
        private readonly Dictionary<string, EditSuggestionRecipient> map;
        public FakeRecipientResolver(Dictionary<string, EditSuggestionRecipient> map) => this.map = map;

        public Task<EditSuggestionRecipient> ResolveAsync(string? userId, CancellationToken cancellationToken = default)
            => Task.FromResult(userId is not null && map.TryGetValue(userId, out var r) ? r : default);
    }

    // ---- Test infrastructure ---------------------------------------------

    private static SqlServerDataContext CreateDb()
    {
        var options = new DbContextOptionsBuilder<SqlServerDataContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;
        return new SqlServerDataContext(options);
    }

    private static IChangeFactory CreateFactory()
    {
        var builders = new IChangeBuilder[]
        {
            new ChangeBuilder<ReleaseFieldsDetails>(
                ReleaseFieldsUpdate.Key,
                (d, opts) => new ReleaseFieldsUpdate(d, opts)),
        };
        return new ChangeFactory(builders);
    }

    private static void SeedRelease(SqlServerDataContext db)
    {
        var mediaItem = new MediaItem
        {
            Slug = "the-movie",
            Title = "The Movie",
            FullTitle = "The Movie (2020)",
            Year = 2020,
            Type = "movie",
        };
        var release = new Release
        {
            Slug = "the-release",
            Title = "Original Title",
            RegionCode = "US",
            Locale = "en-US",
            Year = 2020,
            ReleaseDate = new DateTimeOffset(2020, 5, 15, 0, 0, 0, TimeSpan.Zero),
            MediaItem = mediaItem,
        };
        mediaItem.Releases.Add(release);
        db.Add(mediaItem);
        db.SaveChanges();
    }

    private static ReleaseFieldsDetails MakeProposed(string title = "Updated Title") => new(
        MediaItemSlug: "the-movie",
        BoxsetSlug: null,
        ReleaseSlug: "the-release",
        Title: title,
        RegionCode: "US",
        Locale: "en-US",
        Year: 2020,
        Upc: null,
        Isbn: null,
        Asin: null,
        ReleaseDate: new DateTimeOffset(2020, 5, 15, 0, 0, 0, TimeSpan.Zero));

    private static ReleaseFieldsDetails MakeSnapshot() => new(
        MediaItemSlug: "the-movie",
        BoxsetSlug: null,
        ReleaseSlug: "the-release",
        Title: "Original Title",
        RegionCode: "US",
        Locale: "en-US",
        Year: 2020,
        Upc: null,
        Isbn: null,
        Asin: null,
        ReleaseDate: new DateTimeOffset(2020, 5, 15, 0, 0, 0, TimeSpan.Zero));

    private static FakeRecipientResolver UserResolver(string userId = "user-1", string email = "user@example.com", string name = "Test User")
        => new(new Dictionary<string, EditSuggestionRecipient> { [userId] = new EditSuggestionRecipient(email, name) });

    // ---- Submit ----------------------------------------------------------

    [Test]
    public async Task SubmitAsync_FiresSubmittedOnce_WithResolvedRecipient()
    {
        using var db = CreateDb();
        var notifications = new RecordingNotificationService();
        var service = new EditSuggestionService(
            db, CreateFactory(), new EditSuggestionHistoryService(db), notifications, UserResolver());

        var proposed = JsonSerializer.Serialize(MakeProposed(), JsonOptions);
        var suggestion = await service.SubmitAsync("user-1", EditSuggestionSource.Web, null,
            new List<SubmitChangeInput> { new(ReleaseFieldsUpdate.Key, proposed, null) }, CT);

        await Assert.That(notifications.Submitted.Count).IsEqualTo(1);
        await Assert.That(notifications.Submitted[0].SuggestionId).IsEqualTo(suggestion.Id);
        await Assert.That(notifications.Submitted[0].Email).IsEqualTo("user@example.com");
        await Assert.That(notifications.Submitted[0].Name).IsEqualTo("Test User");
        await Assert.That(notifications.Resolved.Count).IsEqualTo(0);
    }

    // ---- Resolution ------------------------------------------------------

    [Test]
    public async Task Resolution_FiresResolvedExactlyOnce_OnFinalApproved_NotPerChange()
    {
        using var db = CreateDb();
        SeedRelease(db);
        var factory = CreateFactory();
        var history = new EditSuggestionHistoryService(db);
        var notifications = new RecordingNotificationService();
        var resolver = UserResolver();
        var submit = new EditSuggestionService(db, factory, history, notifications, resolver);
        var review = new EditSuggestionReviewService(db, factory, history, notifications, resolver);

        var proposed1 = JsonSerializer.Serialize(MakeProposed("Title A"), JsonOptions);
        var proposed2 = JsonSerializer.Serialize(MakeProposed("Title B"), JsonOptions);
        var snapshot1 = JsonSerializer.Serialize(MakeSnapshot(), JsonOptions);
        // Second change's snapshot reflects the state after the first is applied,
        // so approving both in order doesn't trip the TOCTOU conflict check.
        var snapshot2 = JsonSerializer.Serialize(MakeProposed("Title A"), JsonOptions);
        var suggestion = await submit.SubmitAsync("user-1", EditSuggestionSource.Web, null,
            new List<SubmitChangeInput>
            {
                new(ReleaseFieldsUpdate.Key, proposed1, snapshot1),
                new(ReleaseFieldsUpdate.Key, proposed2, snapshot2),
            }, CT);
        var changes = suggestion.Changes.OrderBy(c => c.Ordinal).ToList();

        // Approve first change — bundle goes to InReview (intermediate), no resolved email.
        await review.ApproveChangeAsync(suggestion.Id, changes[0].Id, "admin-1", null, CT);
        await Assert.That(notifications.Resolved.Count).IsEqualTo(0);

        // Approve second — bundle reaches Approved (final).
        await review.ApproveChangeAsync(suggestion.Id, changes[1].Id, "admin-1", null, CT);

        await Assert.That(notifications.Resolved.Count).IsEqualTo(1);
        await Assert.That(notifications.Resolved[0].Status).IsEqualTo(EditSuggestionStatus.Approved);
        await Assert.That(notifications.Resolved[0].Email).IsEqualTo("user@example.com");
    }

    [Test]
    public async Task Resolution_FiresResolvedOnce_OnRejected()
    {
        using var db = CreateDb();
        SeedRelease(db);
        var factory = CreateFactory();
        var history = new EditSuggestionHistoryService(db);
        var notifications = new RecordingNotificationService();
        var resolver = UserResolver();
        var submit = new EditSuggestionService(db, factory, history, notifications, resolver);
        var review = new EditSuggestionReviewService(db, factory, history, notifications, resolver);

        var proposed = JsonSerializer.Serialize(MakeProposed(), JsonOptions);
        var snapshot = JsonSerializer.Serialize(MakeSnapshot(), JsonOptions);
        var suggestion = await submit.SubmitAsync("user-1", EditSuggestionSource.Web, null,
            new List<SubmitChangeInput> { new(ReleaseFieldsUpdate.Key, proposed, snapshot) }, CT);
        var change = suggestion.Changes.First();

        await review.RejectChangeAsync(suggestion.Id, change.Id, "admin-1", "Not accurate", CT);

        await Assert.That(notifications.Resolved.Count).IsEqualTo(1);
        await Assert.That(notifications.Resolved[0].Status).IsEqualTo(EditSuggestionStatus.Rejected);
    }

    [Test]
    public async Task Resolution_FiresResolvedOnce_OnPartiallyApproved()
    {
        using var db = CreateDb();
        SeedRelease(db);
        var factory = CreateFactory();
        var history = new EditSuggestionHistoryService(db);
        var notifications = new RecordingNotificationService();
        var resolver = UserResolver();
        var submit = new EditSuggestionService(db, factory, history, notifications, resolver);
        var review = new EditSuggestionReviewService(db, factory, history, notifications, resolver);

        var proposed1 = JsonSerializer.Serialize(MakeProposed("Title A"), JsonOptions);
        var proposed2 = JsonSerializer.Serialize(MakeProposed("Title B"), JsonOptions);
        var snapshot = JsonSerializer.Serialize(MakeSnapshot(), JsonOptions);
        var suggestion = await submit.SubmitAsync("user-1", EditSuggestionSource.Web, null,
            new List<SubmitChangeInput>
            {
                new(ReleaseFieldsUpdate.Key, proposed1, snapshot),
                new(ReleaseFieldsUpdate.Key, proposed2, snapshot),
            }, CT);
        var changes = suggestion.Changes.OrderBy(c => c.Ordinal).ToList();

        await review.ApproveChangeAsync(suggestion.Id, changes[0].Id, "admin-1", null, CT);
        await review.RejectChangeAsync(suggestion.Id, changes[1].Id, "admin-1", null, CT);

        await Assert.That(notifications.Resolved.Count).IsEqualTo(1);
        await Assert.That(notifications.Resolved[0].Status).IsEqualTo(EditSuggestionStatus.PartiallyApproved);
    }

    [Test]
    public async Task Resolution_DoesNotFireResolved_OnConflicted()
    {
        using var db = CreateDb();
        SeedRelease(db);
        var factory = CreateFactory();
        var history = new EditSuggestionHistoryService(db);
        var notifications = new RecordingNotificationService();
        var resolver = UserResolver();
        var submit = new EditSuggestionService(db, factory, history, notifications, resolver);
        var review = new EditSuggestionReviewService(db, factory, history, notifications, resolver);

        var proposed = JsonSerializer.Serialize(MakeProposed(), JsonOptions);
        var snapshot = JsonSerializer.Serialize(MakeSnapshot(), JsonOptions);
        var suggestion = await submit.SubmitAsync("user-1", EditSuggestionSource.Web, null,
            new List<SubmitChangeInput> { new(ReleaseFieldsUpdate.Key, proposed, snapshot) }, CT);
        var change = suggestion.Changes.First();

        // Drift the release so approval marks the change Conflicted.
        var release = await db.Releases.FirstAsync(r => r.Slug == "the-release");
        release.Title = "Someone Else Changed This";
        await db.SaveChangesAsync();

        await review.ApproveChangeAsync(suggestion.Id, change.Id, "admin-1", null, CT);

        await Assert.That(notifications.Resolved.Count).IsEqualTo(0);
    }

    // ---- Messages --------------------------------------------------------

    [Test]
    public async Task AddMessageAsync_AdminMessage_RoutesToUser()
    {
        using var db = CreateDb();
        var notifications = new RecordingNotificationService();
        var resolver = UserResolver();
        var service = new EditSuggestionService(
            db, CreateFactory(), new EditSuggestionHistoryService(db), notifications, resolver);

        var proposed = JsonSerializer.Serialize(MakeProposed(), JsonOptions);
        var suggestion = await service.SubmitAsync("user-1", EditSuggestionSource.Web, null,
            new List<SubmitChangeInput> { new(ReleaseFieldsUpdate.Key, proposed, null) }, CT);

        await service.AddMessageAsync(suggestion.Id, "admin-9", "user-1", "Need more info", isAdmin: true, CT);

        var msg = notifications.Messages.Single();
        await Assert.That(msg.FromAdmin).IsTrue();
        await Assert.That(msg.Body).IsEqualTo("Need more info");
        await Assert.That(msg.Email).IsEqualTo("user@example.com");
    }

    [Test]
    public async Task AddMessageAsync_UserMessage_RoutesToAdmin()
    {
        using var db = CreateDb();
        var notifications = new RecordingNotificationService();
        var resolver = UserResolver();
        var service = new EditSuggestionService(
            db, CreateFactory(), new EditSuggestionHistoryService(db), notifications, resolver);

        var proposed = JsonSerializer.Serialize(MakeProposed(), JsonOptions);
        var suggestion = await service.SubmitAsync("user-1", EditSuggestionSource.Web, null,
            new List<SubmitChangeInput> { new(ReleaseFieldsUpdate.Key, proposed, null) }, CT);

        await service.AddMessageAsync(suggestion.Id, "user-1", "admin-1", "Here you go", isAdmin: false, CT);

        var msg = notifications.Messages.Single(m => m.Body == "Here you go");
        await Assert.That(msg.FromAdmin).IsFalse();
        await Assert.That(msg.Name).IsEqualTo("Test User");
        await Assert.That(msg.Email).IsEqualTo("user@example.com");
    }

    // ---- DI gate ---------------------------------------------------------

    private static Type? RegisteredImpl(string? mailgunApiKey, bool? enabled)
    {
        var dict = new Dictionary<string, string?>();
        if (mailgunApiKey is not null)
        {
            dict["Mailgun:ApiKey"] = mailgunApiKey;
        }
        if (enabled is not null)
        {
            dict["EditSuggestions:Notifications:Enabled"] = enabled.Value ? "true" : "false";
        }

        var config = new ConfigurationBuilder().AddInMemoryCollection(dict).Build();
        var services = new ServiceCollection();
        services.AddEditSuggestionNotifications(config);
        return services
            .Single(d => d.ServiceType == typeof(IEditSuggestionNotificationService))
            .ImplementationType;
    }

    [Test]
    public async Task DiGate_RealImpl_OnlyWhenMailgunConfiguredAndEnabled()
    {
        await Assert.That(RegisteredImpl("key-123", enabled: true)).IsEqualTo(typeof(EditSuggestionNotificationService));
    }

    [Test]
    public async Task DiGate_Null_WhenMailgunConfiguredButDisabled()
    {
        await Assert.That(RegisteredImpl("key-123", enabled: false)).IsEqualTo(typeof(NullEditSuggestionNotificationService));
    }

    [Test]
    public async Task DiGate_Null_WhenEnabledButMailgunMissing()
    {
        await Assert.That(RegisteredImpl(mailgunApiKey: null, enabled: true)).IsEqualTo(typeof(NullEditSuggestionNotificationService));
    }

    [Test]
    public async Task DiGate_Null_WhenNothingConfigured()
    {
        await Assert.That(RegisteredImpl(mailgunApiKey: null, enabled: null)).IsEqualTo(typeof(NullEditSuggestionNotificationService));
    }

    // ---- Null impl -------------------------------------------------------

    [Test]
    public async Task NullImpl_NeverThrows()
    {
        var nullService = new NullEditSuggestionNotificationService(
            Microsoft.Extensions.Logging.Abstractions.NullLogger<NullEditSuggestionNotificationService>.Instance);
        var suggestion = new EditSuggestion { Id = 1, UserId = "user-1" };

        await nullService.NotifySuggestionSubmittedAsync(suggestion, "e@x.com", "Name", CT);
        await nullService.NotifySuggestionResolvedAsync(suggestion, "e@x.com", CT);
        await nullService.NotifyMessageFromUserAsync(suggestion, "hi", "Name", "e@x.com", CT);
        await nullService.NotifyMessageFromAdminAsync(suggestion, "hi", "e@x.com", CT);

        // Reaching here without throwing is the assertion.
        await Assert.That(true).IsTrue();
    }
}
