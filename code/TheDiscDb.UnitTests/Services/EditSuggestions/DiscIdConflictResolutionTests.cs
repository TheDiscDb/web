namespace TheDiscDb.UnitTests.Services.EditSuggestions;

using System.Linq;
using System.Text.Json;
using System.Threading;
using Microsoft.EntityFrameworkCore;
using TheDiscDb.Data.Changes;
using TheDiscDb.Data.Changes.DiscFields;
using TheDiscDb.InputModels;
using TheDiscDb.Services.EditSuggestions;
using TheDiscDb.Web.Data;

public class DiscIdConflictResolutionTests
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private static readonly CancellationToken CT = CancellationToken.None;

    private const string Hash = "AAAA1111BBBB2222CCCC3333DDDD4444";
    private const string IdA = "A734E4BEE726B8943F2E8817E3956EFC5F786C8B";
    private const string IdB = "91C2EB717C4323D8807C01BA79011A6B";

    private static SqlServerDataContext CreateDb() =>
        new(new DbContextOptionsBuilder<SqlServerDataContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

    private static IChangeFactory CreateFactory() => new ChangeFactory(new IChangeBuilder[]
    {
        new ChangeBuilder<DiscFieldsDetails>(DiscFieldsUpdate.Key, (d, opts) => new DiscFieldsUpdate(d, opts)),
    });

    // Two releases of the same movie whose discs share one canonical Disc (same content). Release A
    // stores IdA; release B (a re-pressed edition) stores nothing. A boxset release also shares the
    // same canonical disc (a copied "existing disc") — it must never be offered as an attribution
    // target, so it's here to prove it gets excluded.
    private static (int ReleaseDiscBId, int BoxsetReleaseDiscId, SqlServerDataContext Db) Seed()
    {
        var db = CreateDb();
        var disc = new Disc { Format = "Blu-ray", ContentHash = Hash };

        var media = new MediaItem { Slug = "the-movie", Title = "The Movie", Year = 2020, Type = "movie" };
        var releaseA = new Release { Slug = "release-a", Title = "A", Year = 2020, MediaItem = media };
        releaseA.Discs.Add(new ReleaseDisc { Slug = "disc-1", Index = 1, Name = "Disc 1", Disc = disc, GlobalDiscId = IdA });
        var releaseB = new Release { Slug = "release-b", Title = "B", Year = 2021, MediaItem = media };
        var rdB = new ReleaseDisc { Slug = "disc-1", Index = 1, Name = "Disc 1", Disc = disc };
        releaseB.Discs.Add(rdB);
        media.Releases.Add(releaseA);
        media.Releases.Add(releaseB);

        // A boxset whose (single) release includes the same canonical disc, copied from the item.
        var boxsetRelease = new Release { Slug = "the-boxset", Title = "The Boxset", Year = 2022 };
        var rdBoxset = new ReleaseDisc { Slug = "disc-1", Index = 1, Name = "Disc 1", Disc = disc };
        boxsetRelease.Discs.Add(rdBoxset);
        var boxset = new Boxset { Slug = "the-boxset", Title = "The Boxset", Release = boxsetRelease };

        db.Add(media);
        db.Add(boxset);
        db.SaveChanges();
        return (rdB.Id, rdBoxset.Id, db);
    }

    private static async Task<EditSuggestionChange> FileConflictAsync(SqlServerDataContext db, EditSuggestionReviewService review, IChangeFactory factory)
    {
        var history = new EditSuggestionHistoryService(db);
        var submit = new EditSuggestionService(db, factory, history);

        // A change targeting release A proposing a DIFFERENT id (IdB) — conflicts because A has IdA.
        var proposed = new DiscFieldsDetails("the-movie", null, "release-a", "disc-1", 1, "Disc 1", "Blu-ray", Hash, IdB);
        var snapshot = proposed with { GlobalDiscId = null };
        var suggestion = await submit.SubmitAsync("user-1", EditSuggestionSource.Web, null,
            new List<SubmitChangeInput> { new(DiscFieldsUpdate.Key, JsonSerializer.Serialize(proposed, JsonOptions), JsonSerializer.Serialize(snapshot, JsonOptions)) }, CT);

        var change = suggestion.Changes.First();
        var approved = await review.ApproveChangeAsync(suggestion.Id, change.Id, "admin-1", null, CT);
        await Assert.That(approved!.Status).IsEqualTo(EditSuggestionChangeStatus.Conflicted);
        return approved;
    }

    [Test]
    public async Task GetDiscIdConflictContext_ReturnsSubmittedIdAndCandidates()
    {
        var (_, _, db) = Seed();
        var factory = CreateFactory();
        var review = new EditSuggestionReviewService(db, factory, new EditSuggestionHistoryService(db));
        var change = await FileConflictAsync(db, review, factory);

        var ctx = await review.GetDiscIdConflictContextAsync(change.SuggestionId, change.Id, CT);

        await Assert.That(ctx).IsNotNull();
        await Assert.That(ctx!.SubmittedGlobalDiscId).IsEqualTo(IdB);
        await Assert.That(ctx.TargetCurrentGlobalDiscId).IsEqualTo(IdA);
        // Only the two ITEM release-discs (A with IdA, B with none) are candidates — the boxset
        // release-disc that shares the same canonical disc is excluded (it inherits, never owns).
        await Assert.That(ctx.Candidates.Count).IsEqualTo(2);
        await Assert.That(ctx.Candidates.Any(c => c.ReleaseSlug == "release-b" && c.CurrentGlobalDiscId == null)).IsTrue();
        await Assert.That(ctx.Candidates.Any(c => c.ReleaseSlug == "the-boxset")).IsFalse();
    }

    [Test]
    public async Task GetDiscIdConflictContext_ExcludesBoxsetReleaseDiscs()
    {
        var (_, boxsetReleaseDiscId, db) = Seed();
        var factory = CreateFactory();
        var review = new EditSuggestionReviewService(db, factory, new EditSuggestionHistoryService(db));
        var change = await FileConflictAsync(db, review, factory);

        var ctx = await review.GetDiscIdConflictContextAsync(change.SuggestionId, change.Id, CT);

        await Assert.That(ctx).IsNotNull();
        await Assert.That(ctx!.Candidates.Any(c => c.ReleaseDiscId == boxsetReleaseDiscId)).IsFalse();
    }

    [Test]
    public async Task AttributeDiscId_AssignsSubmittedIdToChosenReleaseDisc_AndLeavesOriginalIntact()
    {
        var (releaseDiscBId, _, db) = Seed();
        var factory = CreateFactory();
        var review = new EditSuggestionReviewService(db, factory, new EditSuggestionHistoryService(db));
        var change = await FileConflictAsync(db, review, factory);

        // Attribute IdB to release B (the re-pressed sibling that had no id).
        var result = await review.AttributeDiscIdAsync(change.SuggestionId, change.Id, releaseDiscBId, "admin-1", CT);

        await Assert.That(result!.Status).IsEqualTo(EditSuggestionChangeStatus.Applied);

        var rdB = await db.Set<ReleaseDisc>().FirstAsync(rd => rd.Id == releaseDiscBId);
        await Assert.That(rdB.GlobalDiscId).IsEqualTo(IdB);

        // Release A keeps its original id untouched.
        var rdA = await db.Set<ReleaseDisc>().FirstAsync(rd => rd.Release!.Slug == "release-a");
        await Assert.That(rdA.GlobalDiscId).IsEqualTo(IdA);
    }

    [Test]
    public async Task AttributeDiscId_Conflicts_WhenDestinationAlreadyHasDifferentId()
    {
        var (_, _, db) = Seed();
        var factory = CreateFactory();
        var review = new EditSuggestionReviewService(db, factory, new EditSuggestionHistoryService(db));
        var change = await FileConflictAsync(db, review, factory);

        // Attributing to release A (which already has IdA) must not overwrite it.
        var rdAId = await db.Set<ReleaseDisc>().Where(rd => rd.Release!.Slug == "release-a").Select(rd => rd.Id).FirstAsync();
        var result = await review.AttributeDiscIdAsync(change.SuggestionId, change.Id, rdAId, "admin-1", CT);

        await Assert.That(result!.Status).IsEqualTo(EditSuggestionChangeStatus.Conflicted);
        var rdA = await db.Set<ReleaseDisc>().FirstAsync(rd => rd.Id == rdAId);
        await Assert.That(rdA.GlobalDiscId).IsEqualTo(IdA);
    }

    [Test]
    public async Task AttributeDiscId_Conflicts_WhenDestinationIsBoxset()
    {
        var (_, boxsetReleaseDiscId, db) = Seed();
        var factory = CreateFactory();
        var review = new EditSuggestionReviewService(db, factory, new EditSuggestionHistoryService(db));
        var change = await FileConflictAsync(db, review, factory);

        // A boxset release-disc inherits its id — refuse to attribute even if explicitly requested.
        var result = await review.AttributeDiscIdAsync(change.SuggestionId, change.Id, boxsetReleaseDiscId, "admin-1", CT);

        await Assert.That(result!.Status).IsEqualTo(EditSuggestionChangeStatus.Conflicted);
        var rdBoxset = await db.Set<ReleaseDisc>().FirstAsync(rd => rd.Id == boxsetReleaseDiscId);
        await Assert.That(rdBoxset.GlobalDiscId).IsNull();
    }

    [Test]
    public async Task SwapDiscId_AssignsSubmittedToTarget_AndMovesDisplacedIdToSibling()
    {
        var (releaseDiscBId, _, db) = Seed();
        var factory = CreateFactory();
        var review = new EditSuggestionReviewService(db, factory, new EditSuggestionHistoryService(db));
        var change = await FileConflictAsync(db, review, factory);

        // Target A has IdA; submitted is IdB. Swap: A becomes IdB, and A's old IdA moves to sibling B.
        var result = await review.SwapDiscIdAsync(change.SuggestionId, change.Id, releaseDiscBId, "admin-1", CT);

        await Assert.That(result!.Status).IsEqualTo(EditSuggestionChangeStatus.Applied);

        var rdA = await db.Set<ReleaseDisc>().FirstAsync(rd => rd.Release!.Slug == "release-a");
        await Assert.That(rdA.GlobalDiscId).IsEqualTo(IdB);

        var rdB = await db.Set<ReleaseDisc>().FirstAsync(rd => rd.Id == releaseDiscBId);
        await Assert.That(rdB.GlobalDiscId).IsEqualTo(IdA);

        // A second applied change was recorded for the sibling so the displaced id syncs to /data.
        var changes = await db.EditSuggestionChanges
            .Where(c => c.SuggestionId == change.SuggestionId).ToListAsync();
        await Assert.That(changes.Count).IsEqualTo(2);
        await Assert.That(changes.All(c => c.Status == EditSuggestionChangeStatus.Applied)).IsTrue();
        var siblingChange = changes.First(c => c.Id != change.Id);
        var siblingProposed = JsonSerializer.Deserialize<DiscFieldsDetails>(siblingChange.ProposedJson, JsonOptions);
        await Assert.That(siblingProposed!.ReleaseSlug).IsEqualTo("release-b");
        await Assert.That(siblingProposed.GlobalDiscId).IsEqualTo(IdA);

        // The primary change is an OVERWRITE: its snapshot records the displaced IdA (what authorises
        // the file applier to overwrite), and its proposed carries IdB.
        var primaryProposed = JsonSerializer.Deserialize<DiscFieldsDetails>(change.ProposedJson, JsonOptions);
        var primarySnapshot = JsonSerializer.Deserialize<DiscFieldsDetails>(change.OriginalSnapshotJson!, JsonOptions);
        await Assert.That(primaryProposed!.GlobalDiscId).IsEqualTo(IdB);
        await Assert.That(primarySnapshot!.GlobalDiscId).IsEqualTo(IdA);
    }

    [Test]
    public async Task SwapDiscId_Conflicts_WhenDestinationIsBoxset()
    {
        var (_, boxsetReleaseDiscId, db) = Seed();
        var factory = CreateFactory();
        var review = new EditSuggestionReviewService(db, factory, new EditSuggestionHistoryService(db));
        var change = await FileConflictAsync(db, review, factory);

        var result = await review.SwapDiscIdAsync(change.SuggestionId, change.Id, boxsetReleaseDiscId, "admin-1", CT);

        await Assert.That(result!.Status).IsEqualTo(EditSuggestionChangeStatus.Conflicted);
        // Nothing moved: A keeps IdA, the boxset stays empty.
        var rdA = await db.Set<ReleaseDisc>().FirstAsync(rd => rd.Release!.Slug == "release-a");
        await Assert.That(rdA.GlobalDiscId).IsEqualTo(IdA);
        var rdBoxset = await db.Set<ReleaseDisc>().FirstAsync(rd => rd.Id == boxsetReleaseDiscId);
        await Assert.That(rdBoxset.GlobalDiscId).IsNull();
    }
}
