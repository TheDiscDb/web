namespace TheDiscDb.UnitTests.Services.EditSuggestions;

using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using TheDiscDb.Data.Changes;
using TheDiscDb.Data.Changes.DiscItemFields;
using TheDiscDb.Data.Changes.DiscPartial;
using TheDiscDb.InputModels;
using TheDiscDb.Services.EditSuggestions;
using TheDiscDb.Web.Data;

public class AutomaticPartialClearTests
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    [Test]
    public async Task ApprovingFirstIdentifiedItem_ClearsAutomaticMarkerAndAddsSyncChange()
    {
        await using var db = CreateDb();
        SeedEmptyDisc(db);
        var factory = new ChangeFactory(
        [
            new ChangeBuilder<DiscItemFieldsDetails>(
                DiscItemAdd.Key,
                (details, options) => new DiscItemAdd(details, options)),
            new ChangeBuilder<DiscPartialDetails>(
                DiscPartialUpdate.Key,
                (details, options) => new DiscPartialUpdate(details, options)),
        ]);
        var history = new EditSuggestionHistoryService(db);
        var submit = new EditSuggestionService(db, factory, history);
        var review = new EditSuggestionReviewService(db, factory, history);
        var proposed = new DiscItemFieldsDetails(
            MediaItemSlug: "movie",
            BoxsetSlug: null,
            ReleaseSlug: "release",
            DiscSlug: "disc-one",
            TitleIndex: 1,
            Comment: null,
            SourceFile: "00001.mpls",
            SegmentMap: "1",
            Duration: "01:30:00",
            HasItem: true,
            ItemTitle: "Main Feature",
            ItemType: "MainMovie",
            ItemDescription: null,
            ItemSeason: null,
            ItemEpisode: null);
        var suggestion = await submit.SubmitAsync(
            "user",
            EditSuggestionSource.Web,
            null,
            [new SubmitChangeInput(DiscItemAdd.Key, JsonSerializer.Serialize(proposed, JsonOptions), null)],
            CancellationToken.None);

        var approved = await review.ApproveChangeAsync(
            suggestion.Id,
            suggestion.Changes.Single().Id,
            "admin",
            null,
            CancellationToken.None);

        await Assert.That(approved?.Status).IsEqualTo(EditSuggestionChangeStatus.Applied);
        var disc = await db.Discs.Include(d => d.Titles).ThenInclude(t => t.Item).SingleAsync();
        await Assert.That(disc.Partial).IsNull();
        await Assert.That(disc.Titles.Count(t => t.Item != null)).IsEqualTo(1);

        var changes = await db.EditSuggestionChanges
            .Where(c => c.SuggestionId == suggestion.Id)
            .OrderBy(c => c.Ordinal)
            .ToListAsync();
        await Assert.That(changes.Count).IsEqualTo(2);
        await Assert.That(changes[1].Type).IsEqualTo(DiscPartialUpdate.Key);
        await Assert.That(changes[1].Status).IsEqualTo(EditSuggestionChangeStatus.Applied);
        await Assert.That(changes[1].SyncedToFilesAt).IsNull();
    }

    [Test]
    public async Task IdentifyingExistingTitle_ClearsAutomaticMarkerAndAddsSyncChange()
    {
        await using var db = CreateDb();
        SeedEmptyDisc(db, includeRawTitle: true);
        var factory = new ChangeFactory(
        [
            new ChangeBuilder<DiscItemFieldsDetails>(
                DiscItemFieldsUpdate.Key,
                (details, options) => new DiscItemFieldsUpdate(details, options)),
            new ChangeBuilder<DiscPartialDetails>(
                DiscPartialUpdate.Key,
                (details, options) => new DiscPartialUpdate(details, options)),
        ]);
        var history = new EditSuggestionHistoryService(db);
        var submit = new EditSuggestionService(db, factory, history);
        var review = new EditSuggestionReviewService(db, factory, history);
        var proposed = new DiscItemFieldsDetails(
            MediaItemSlug: "movie",
            BoxsetSlug: null,
            ReleaseSlug: "release",
            DiscSlug: "disc-one",
            TitleIndex: 1,
            Comment: null,
            SourceFile: "00001.mpls",
            SegmentMap: "1",
            Duration: "01:30:00",
            HasItem: true,
            ItemTitle: "Main Feature",
            ItemType: "MainMovie",
            ItemDescription: null,
            ItemSeason: null,
            ItemEpisode: null);
        var snapshot = proposed with
        {
            HasItem = false,
            ItemTitle = null,
            ItemType = null,
        };
        var suggestion = await submit.SubmitAsync(
            "user",
            EditSuggestionSource.Web,
            null,
            [
                new SubmitChangeInput(
                    DiscItemFieldsUpdate.Key,
                    JsonSerializer.Serialize(proposed, JsonOptions),
                    JsonSerializer.Serialize(snapshot, JsonOptions)),
            ],
            CancellationToken.None);

        var approved = await review.ApproveChangeAsync(
            suggestion.Id,
            suggestion.Changes.Single().Id,
            "admin",
            null,
            CancellationToken.None);

        await Assert.That(approved?.Status).IsEqualTo(EditSuggestionChangeStatus.Applied);
        var disc = await db.Discs.Include(d => d.Titles).ThenInclude(t => t.Item).SingleAsync();
        await Assert.That(disc.Partial).IsNull();
        await Assert.That(disc.Titles.Count(t => t.Item != null)).IsEqualTo(1);
        await Assert.That(await db.EditSuggestionChanges.CountAsync()).IsEqualTo(2);
    }

    private static SqlServerDataContext CreateDb()
    {
        var options = new DbContextOptionsBuilder<SqlServerDataContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new SqlServerDataContext(options);
    }

    private static void SeedEmptyDisc(SqlServerDataContext db, bool includeRawTitle = false)
    {
        var disc = new Disc
        {
            Format = "Blu-ray",
            ContentHash = "HASH",
            Partial = PartialStateLifecycle.CreateAutomaticUnidentified(),
        };
        var releaseDisc = new ReleaseDisc
        {
            Index = 0,
            Slug = "disc-one",
            Name = "Disc One",
            Disc = disc,
        };
        if (includeRawTitle)
        {
            disc.Titles.Add(new Title
            {
                Index = 1,
                SourceFile = "00001.mpls",
                SegmentMap = "1",
                Duration = "01:30:00",
            });
        }
        var release = new Release
        {
            Slug = "release",
            Title = "Release",
            MediaItem = new MediaItem
            {
                Slug = "movie",
                Title = "Movie",
                FullTitle = "Movie (2020)",
                Type = "movie",
                Year = 2020,
            },
        };
        release.Discs.Add(releaseDisc);
        release.MediaItem.Releases.Add(release);
        db.Add(release.MediaItem);
        db.SaveChanges();
    }
}
