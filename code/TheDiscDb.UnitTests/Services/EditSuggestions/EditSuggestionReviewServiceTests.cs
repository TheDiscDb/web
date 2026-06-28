namespace TheDiscDb.UnitTests.Services.EditSuggestions;

using System.Text.Json;
using System.Threading;
using Microsoft.EntityFrameworkCore;
using TheDiscDb.Data.Changes;
using TheDiscDb.Data.Changes.ReleaseFields;
using TheDiscDb.InputModels;
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

    private async Task<(SqlServerDataContext Db, EditSuggestionReviewService ReviewService, EditSuggestion Suggestion)> SetupAsync()
    {
        var db = CreateDb();
        SeedRelease(db);
        var factory = CreateFactory();
        var history = new EditSuggestionHistoryService(db);
        var submitService = new EditSuggestionService(db, factory, history);
        var reviewService = new EditSuggestionReviewService(db, factory, history);

        var proposed = JsonSerializer.Serialize(MakeProposed(), JsonOptions);
        var snapshot = JsonSerializer.Serialize(MakeSnapshot(), JsonOptions);
        var suggestion = await submitService.SubmitAsync("user-1", EditSuggestionSource.Web, null,
            new List<SubmitChangeInput> { new(ReleaseFieldsUpdate.Key, proposed, snapshot) }, CT);

        return (db, reviewService, suggestion);
    }

    [Test]
    public async Task ApproveChangeAsync_AppliesMutationAndUpdatesStatus()
    {
        var (db, reviewService, suggestion) = await SetupAsync();
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
        var (db, reviewService, suggestion) = await SetupAsync();
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
        var (db, reviewService, suggestion) = await SetupAsync();
        var change = suggestion.Changes.First();

        var result = await reviewService.RejectChangeAsync(suggestion.Id, change.Id, "admin-1", "Not accurate", CT);

        await Assert.That(result).IsNotNull();
        await Assert.That(result!.Status).IsEqualTo(EditSuggestionChangeStatus.Rejected);
        await Assert.That(result.AdminNote).IsEqualTo("Not accurate");

        // Bundle should be rejected (all changes rejected).
        var reloaded = await db.EditSuggestions.FirstAsync(s => s.Id == suggestion.Id);
        await Assert.That(reloaded.Status).IsEqualTo(EditSuggestionStatus.Rejected);
    }

    [Test]
    public async Task BundleStatusRollup_PartiallyApproved_WhenMixed()
    {
        var db = CreateDb();
        SeedRelease(db);
        var factory = CreateFactory();
        var history = new EditSuggestionHistoryService(db);
        var submitService = new EditSuggestionService(db, factory, history);
        var reviewService = new EditSuggestionReviewService(db, factory, history);

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
        await reviewService.RejectChangeAsync(suggestion.Id, changes[1].Id, "admin-1", null, CT);

        var reloaded = await db.EditSuggestions.FirstAsync(s => s.Id == suggestion.Id);
        await Assert.That(reloaded.Status).IsEqualTo(EditSuggestionStatus.PartiallyApproved);
    }

    [Test]
    public async Task ApproveChangeAsync_ReturnsNull_WhenAlreadyApplied()
    {
        var (db, reviewService, suggestion) = await SetupAsync();
        var change = suggestion.Changes.First();

        await reviewService.ApproveChangeAsync(suggestion.Id, change.Id, "admin-1", null, CT);
        var result = await reviewService.ApproveChangeAsync(suggestion.Id, change.Id, "admin-1", null, CT);

        await Assert.That(result).IsNull();
    }
}
