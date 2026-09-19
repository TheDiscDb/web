namespace TheDiscDb.UnitTests.Services.EditSuggestions;

using System.Text.Json;
using System.Threading;
using Microsoft.EntityFrameworkCore;
using TheDiscDb.Data.Changes;
using TheDiscDb.Data.Changes.ReleaseFields;
using TheDiscDb.InputModels;
using TheDiscDb.Services;
using TheDiscDb.Services.EditSuggestions;
using TheDiscDb.Web.Data;

public class EditSuggestionReviewServiceTests
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private static readonly CancellationToken CT = CancellationToken.None;

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

    private async Task<(SqlServerDataContext Db, EditSuggestionReviewService ReviewService, EditSuggestion Suggestion, RecordingMessageService Messages)> SetupAsync()
    {
        var db = CreateDb();
        SeedRelease(db);
        var factory = CreateFactory();
        var history = new EditSuggestionHistoryService(db);
        var submitService = new EditSuggestionService(db, factory, history);
        var messages = new RecordingMessageService(db);
        var reviewService = new EditSuggestionReviewService(
            db, factory, history, messageService: messages);

        var proposed = JsonSerializer.Serialize(MakeProposed(), JsonOptions);
        var snapshot = JsonSerializer.Serialize(MakeSnapshot(), JsonOptions);
        var suggestion = await submitService.SubmitAsync("user-1", EditSuggestionSource.Web, null,
            new List<SubmitChangeInput> { new(ReleaseFieldsUpdate.Key, proposed, snapshot) }, CT);

        return (db, reviewService, suggestion, messages);
    }

    [Test]
    public async Task ApproveChangeAsync_AppliesMutationAndUpdatesStatus()
    {
        var (db, reviewService, suggestion, _) = await SetupAsync();
        var change = suggestion.Changes.First();

        var result = await reviewService.ApproveChangeAsync(suggestion.Id, change.Id, "admin-1", "Looks good", CT);

        await Assert.That(result).IsNotNull();
        await Assert.That(result!.Status).IsEqualTo(EditSuggestionChangeStatus.Applied);
        await Assert.That(result.AppliedByUserId).IsEqualTo("admin-1");
        await Assert.That(result.AdminNote).IsEqualTo("Looks good");

        // Verify the DB was actually mutated.
        var release = await db.Releases.FirstAsync(r => r.Slug == "the-release");
        await Assert.That(release.Title).IsEqualTo("Updated Title");

        // Bundle should be fully approved.
        var reloaded = await db.EditSuggestions.FirstAsync(s => s.Id == suggestion.Id);
        await Assert.That(reloaded.Status).IsEqualTo(EditSuggestionStatus.Approved);
    }

    [Test]
    public async Task ApproveChangeAsync_MarksConflictedWhenSnapshotDrifts()
    {
        var (db, reviewService, suggestion, _) = await SetupAsync();
        var change = suggestion.Changes.First();

        // Simulate someone else changing the release title (drift from snapshot).
        var release = await db.Releases.FirstAsync(r => r.Slug == "the-release");
        release.Title = "Someone Else Changed This";
        await db.SaveChangesAsync();

        var result = await reviewService.ApproveChangeAsync(suggestion.Id, change.Id, "admin-1", null, CT);

        await Assert.That(result).IsNotNull();
        await Assert.That(result!.Status).IsEqualTo(EditSuggestionChangeStatus.Conflicted);
        await Assert.That(result.ConflictReason).IsNotNull();

        // Bundle should be marked conflicted.
        var reloaded = await db.EditSuggestions.FirstAsync(s => s.Id == suggestion.Id);
        await Assert.That(reloaded.Status).IsEqualTo(EditSuggestionStatus.Conflicted);
    }

    [Test]
    public async Task RejectChangeAsync_TransitionsToPendingRejected()
    {
        var (db, reviewService, suggestion, messages) = await SetupAsync();
        var change = suggestion.Changes.First();

        var result = await reviewService.RejectChangeAsync(suggestion.Id, change.Id, "admin-1", "Not accurate", CT);

        await Assert.That(result).IsNotNull();
        await Assert.That(result!.Status).IsEqualTo(EditSuggestionChangeStatus.Rejected);
        await Assert.That(result.AdminNote).IsEqualTo("Not accurate");

        // Bundle should be rejected (all changes rejected).
        var reloaded = await db.EditSuggestions.FirstAsync(s => s.Id == suggestion.Id);
        await Assert.That(reloaded.Status).IsEqualTo(EditSuggestionStatus.Rejected);
        var summary = await db.UserMessages.SingleAsync(message =>
            message.EditSuggestionId == suggestion.Id &&
            message.Purpose == UserMessagePurpose.ResolutionSummary);
        await Assert.That(summary.Message).Contains("Not accurate");

        var retry = await reviewService.RejectChangeAsync(
            suggestion.Id, change.Id, "admin-1", "Not accurate", CT);
        await Assert.That(retry).IsNull();
        await Assert.That(await db.UserMessages.CountAsync(message =>
            message.EditSuggestionId == suggestion.Id &&
            message.Purpose == UserMessagePurpose.ResolutionSummary)).IsEqualTo(1);
    }

    [Test]
    public async Task RejectChangeAsync_TransitionsAppliedToRejectedWithoutUndoingMutation()
    {
        var (db, reviewService, suggestion, _) = await SetupAsync();
        var change = suggestion.Changes.First();

        await reviewService.ApproveChangeAsync(suggestion.Id, change.Id, "admin-1", null, CT);
        var result = await reviewService.RejectChangeAsync(
            suggestion.Id, change.Id, "admin-2", "Approved in error", CT);

        await Assert.That(result).IsNotNull();
        await Assert.That(result!.Status).IsEqualTo(EditSuggestionChangeStatus.Rejected);
        await Assert.That(result.AdminNote).IsEqualTo("Approved in error");
        await Assert.That(result.AppliedAt).IsNotNull();
        await Assert.That(result.AppliedByUserId).IsEqualTo("admin-1");

        var release = await db.Releases.FirstAsync(r => r.Slug == "the-release");
        await Assert.That(release.Title).IsEqualTo("Updated Title");

        var reloaded = await db.EditSuggestions.FirstAsync(s => s.Id == suggestion.Id);
        await Assert.That(reloaded.Status).IsEqualTo(EditSuggestionStatus.Rejected);
        await Assert.That(reloaded.ReviewedByUserId).IsEqualTo("admin-2");
    }

    [Test]
    public async Task RejectAllChangesAsync_TransitionsAppliedBundleToRejectedWithoutUndoingMutation()
    {
        var (db, reviewService, suggestion, _) = await SetupAsync();
        var change = suggestion.Changes.First();

        await reviewService.ApproveChangeAsync(suggestion.Id, change.Id, "admin-1", null, CT);
        var result = await reviewService.RejectAllChangesAsync(
            suggestion.Id, "admin-2", "Suggestion approved in error", CT);

        await Assert.That(result).IsNotNull();
        await Assert.That(result!.Status).IsEqualTo(EditSuggestionStatus.Rejected);
        await Assert.That(result.Changes.All(c => c.Status == EditSuggestionChangeStatus.Rejected)).IsTrue();

        var release = await db.Releases.FirstAsync(r => r.Slug == "the-release");
        await Assert.That(release.Title).IsEqualTo("Updated Title");
    }

    [Test]
    public async Task BundleStatusRollup_PartiallyApproved_WhenMixed()
    {
        var db = CreateDb();
        SeedRelease(db);
        var factory = CreateFactory();
        var history = new EditSuggestionHistoryService(db);
        var submitService = new EditSuggestionService(db, factory, history);
        var messages = new RecordingMessageService(db);
        var reviewService = new EditSuggestionReviewService(
            db, factory, history, messageService: messages);

        var proposed1 = JsonSerializer.Serialize(MakeProposed("Title A"), JsonOptions);
        var proposed2 = JsonSerializer.Serialize(MakeProposed("Title B"), JsonOptions);
        var snapshot = JsonSerializer.Serialize(MakeSnapshot(), JsonOptions);
        var suggestion = await submitService.SubmitAsync("user-1", EditSuggestionSource.Web, null,
            new List<SubmitChangeInput>
            {
                new(ReleaseFieldsUpdate.Key, proposed1, snapshot),
                new(ReleaseFieldsUpdate.Key, proposed2, snapshot),
            }, CT);

        var changes = suggestion.Changes.OrderBy(c => c.Ordinal).ToList();

        // Approve first, reject second.
        await reviewService.ApproveChangeAsync(suggestion.Id, changes[0].Id, "admin-1", null, CT);
        await reviewService.RejectChangeAsync(
            suggestion.Id, changes[1].Id, "admin-1", "Incorrect title", CT);

        var reloaded = await db.EditSuggestions.FirstAsync(s => s.Id == suggestion.Id);
        await Assert.That(reloaded.Status).IsEqualTo(EditSuggestionStatus.PartiallyApproved);
        var summary = await db.UserMessages.SingleAsync(message =>
            message.EditSuggestionId == suggestion.Id &&
            message.Purpose == UserMessagePurpose.ResolutionSummary);
        await Assert.That(summary.Message).Contains("Incorrect title");
    }

    [Test]
    public async Task ApproveChangeAsync_ReturnsNull_WhenAlreadyApplied()
    {
        var (db, reviewService, suggestion, _) = await SetupAsync();
        var change = suggestion.Changes.First();

        await reviewService.ApproveChangeAsync(suggestion.Id, change.Id, "admin-1", null, CT);
        var result = await reviewService.ApproveChangeAsync(suggestion.Id, change.Id, "admin-1", null, CT);

        await Assert.That(result).IsNull();
    }

    [Test]
    public async Task ApproveChangeAsync_ReturnsNull_WhenSuggestionWithdrawn()
    {
        var (db, reviewService, suggestion, _) = await SetupAsync();
        var change = suggestion.Changes.First();

        // User withdrew the suggestion; its change is still Pending.
        var entity = await db.EditSuggestions.FirstAsync(s => s.Id == suggestion.Id);
        entity.Status = EditSuggestionStatus.Withdrawn;
        await db.SaveChangesAsync();

        var result = await reviewService.ApproveChangeAsync(suggestion.Id, change.Id, "admin-1", null, CT);

        await Assert.That(result).IsNull();

        // Change must remain Pending and the release untouched.
        var reloadedChange = await db.Set<EditSuggestionChange>().FirstAsync(c => c.Id == change.Id);
        await Assert.That(reloadedChange.Status).IsEqualTo(EditSuggestionChangeStatus.Pending);
        var release = await db.Releases.FirstAsync(r => r.Slug == "the-release");
        await Assert.That(release.Title).IsEqualTo("Original Title");
    }

    [Test]
    public async Task RejectChangeAsync_ReturnsNull_WhenSuggestionWithdrawn()
    {
        var (db, reviewService, suggestion, _) = await SetupAsync();
        var change = suggestion.Changes.First();

        var entity = await db.EditSuggestions.FirstAsync(s => s.Id == suggestion.Id);
        entity.Status = EditSuggestionStatus.Withdrawn;
        await db.SaveChangesAsync();

        var result = await reviewService.RejectChangeAsync(suggestion.Id, change.Id, "admin-1", "no", CT);

        await Assert.That(result).IsNull();

        var reloadedChange = await db.Set<EditSuggestionChange>().FirstAsync(c => c.Id == change.Id);
        await Assert.That(reloadedChange.Status).IsEqualTo(EditSuggestionChangeStatus.Pending);
    }

    [Test]
    public async Task RequestChangesAsync_PreservesOriginalChangeAndCanLaterApprove()
    {
        var (db, reviewService, suggestion, messages) = await SetupAsync();
        var change = suggestion.Changes.Single();
        var originalProposedJson = change.ProposedJson;
        var originalSnapshotJson = change.OriginalSnapshotJson;

        var requested = await reviewService.RequestChangesAsync(
            suggestion.Id, "admin-1", "Please confirm the release title", CT);

        await Assert.That(requested).IsNotNull();
        await Assert.That(requested!.Status).IsEqualTo(EditSuggestionStatus.ChangesRequested);
        await Assert.That(change.Status).IsEqualTo(EditSuggestionChangeStatus.Pending);
        await Assert.That(change.ProposedJson).IsEqualTo(originalProposedJson);
        await Assert.That(change.OriginalSnapshotJson).IsEqualTo(originalSnapshotJson);
        await Assert.That(messages.AdminNotifications).Count().IsEqualTo(1);
        await Assert.That(await db.UserMessages.CountAsync(message =>
            message.EditSuggestionId == suggestion.Id)).IsEqualTo(1);

        var approved = await reviewService.ApproveChangeAsync(
            suggestion.Id, change.Id, "admin-1", null, CT);

        await Assert.That(approved).IsNotNull();
        await Assert.That(approved!.Status).IsEqualTo(EditSuggestionChangeStatus.Applied);
        var reloaded = await db.EditSuggestions.FirstAsync(item => item.Id == suggestion.Id);
        await Assert.That(reloaded.Status).IsEqualTo(EditSuggestionStatus.Approved);
    }

    [Test]
    public async Task RequestChangesAsync_RejectsTerminalSuggestion()
    {
        var (db, reviewService, suggestion, messages) = await SetupAsync();
        suggestion.Status = EditSuggestionStatus.Rejected;
        await db.SaveChangesAsync();

        var result = await reviewService.RequestChangesAsync(
            suggestion.Id, "admin-1", "Reopen this", CT);

        await Assert.That(result).IsNull();
        await Assert.That(messages.AdminNotifications).IsEmpty();
        await Assert.That(await db.UserMessages.CountAsync()).IsEqualTo(0);
    }

    private sealed class RecordingMessageService(SqlServerDataContext database) : IMessageService
    {
        public List<string> AdminNotifications { get; } = [];

        public Task<UserMessage> SendAdminMessageAsync(int contributionId, string fromUserId, string toUserId, string message, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
        public Task<UserMessage> SendUserMessageAsync(int contributionId, string fromUserId, string message, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
        public Task<UserMessage> SendAdminBoxsetMessageAsync(int boxsetId, string fromUserId, string toUserId, string message, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
        public Task<UserMessage> SendUserBoxsetMessageAsync(int boxsetId, string fromUserId, string message, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
        public Task<UserMessage> SendUserEditSuggestionMessageAsync(int suggestionId, string fromUserId, string message, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
        public Task MarkEditSuggestionMessagesAsReadAsync(int suggestionId, string userId, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        public Task NotifyAdminEditSuggestionMessageAsync(
            EditSuggestion suggestion,
            string message,
            CancellationToken cancellationToken = default)
        {
            AdminNotifications.Add(message);
            return Task.CompletedTask;
        }

        public async Task<UserMessage> SendAdminEditSuggestionMessageAsync(
            int suggestionId,
            string fromUserId,
            string message,
            bool sendNotification = true,
            CancellationToken cancellationToken = default)
        {
            var suggestion = await database.EditSuggestions
                .FirstAsync(item => item.Id == suggestionId, cancellationToken);
            var userMessage = new UserMessage
            {
                EditSuggestionId = suggestionId,
                FromUserId = fromUserId,
                ToUserId = suggestion.UserId,
                Message = message,
                Type = UserMessageType.AdminMessage,
                CreatedAt = DateTimeOffset.UtcNow,
            };
            database.UserMessages.Add(userMessage);
            await database.SaveChangesAsync(cancellationToken);
            if (sendNotification)
            {
                AdminNotifications.Add(message);
            }
            return userMessage;
        }
    }
}
