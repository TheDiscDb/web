namespace TheDiscDb.UnitTests.Services.DiscId;

using Microsoft.EntityFrameworkCore;
using TheDiscDb.Data.Changes;
using TheDiscDb.Data.Changes.DiscFields;
using TheDiscDb.InputModels;
using TheDiscDb.Services.DiscId;
using TheDiscDb.Services.EditSuggestions;
using TheDiscDb.UnitTests.Data.Changes;
using TheDiscDb.Web.Data;

public class DiscIdBackfillServiceTests
{
    private const string ContentHash = "AAAA1111BBBB2222CCCC3333DDDD4444";
    private const string DiscId = "A734E4BEE726B8943F2E8817E3956EFC5F786C8B";
    private const string OtherDiscId = "91C2EB717C4323D8807C01BA79011A6B";

    private static DiscIdBackfillService CreateService(SqlServerDataContext db)
    {
        var factory = new ChangeFactory(new IChangeBuilder[]
        {
            new ChangeBuilder<DiscFieldsDetails>(DiscFieldsUpdate.Key, (d, opts) => new DiscFieldsUpdate(d, opts)),
        });
        var history = new EditSuggestionHistoryService(db);
        var editService = new EditSuggestionService(db, factory, history);
        var review = new EditSuggestionReviewService(db, factory, history);
        return new DiscIdBackfillService(db, editService, review);
    }

    private static async Task<string?> ReloadDiscIdAsync(SqlServerDataContext db)
    {
        var rd = await db.Set<ReleaseDisc>().Include(x => x.Disc).FirstAsync(x => x.Slug == ChangeTestSeed.DiscSlug);
        return rd.Disc!.GlobalDiscId;
    }

    [Test]
    public async Task AttachAsync_CleanDisc_WritesDiscId_AndRecordsAppliedSyncEligibleChange()
    {
        using var db = ChangeTestSeed.CreateDbContext();
        var seed = ChangeTestSeed.Seed(db);
        seed.Disc.ContentHash = ContentHash;
        await db.SaveChangesAsync();

        var result = await CreateService(db).AttachAsync("user-1", ContentHash, DiscId, target: null);

        await Assert.That(result.Outcome).IsEqualTo(AttachDiscIdOutcome.Applied);
        await Assert.That(await ReloadDiscIdAsync(db)).IsEqualTo(DiscId);

        // The change must be Applied and unsynced so the /data batch sync picks it up.
        var change = await db.Set<EditSuggestionChange>().FirstAsync();
        await Assert.That(change.Status).IsEqualTo(EditSuggestionChangeStatus.Applied);
        await Assert.That(change.SyncedToFilesAt).IsNull();
    }

    [Test]
    public async Task AttachAsync_SameDiscId_IsIdempotentNoOp()
    {
        using var db = ChangeTestSeed.CreateDbContext();
        var seed = ChangeTestSeed.Seed(db);
        seed.Disc.ContentHash = ContentHash;
        seed.Disc.GlobalDiscId = DiscId;
        await db.SaveChangesAsync();

        var result = await CreateService(db).AttachAsync("user-1", ContentHash, DiscId, target: null);

        await Assert.That(result.Outcome).IsEqualTo(AttachDiscIdOutcome.AlreadyRecorded);
        await Assert.That(await db.Set<EditSuggestionChange>().AnyAsync()).IsFalse();
    }

    [Test]
    public async Task AttachAsync_DifferentExistingDiscId_ConflictsWithoutTouchingDb()
    {
        using var db = ChangeTestSeed.CreateDbContext();
        var seed = ChangeTestSeed.Seed(db);
        seed.Disc.ContentHash = ContentHash;
        seed.Disc.GlobalDiscId = OtherDiscId;
        await db.SaveChangesAsync();

        var result = await CreateService(db).AttachAsync("user-1", ContentHash, DiscId, target: null);

        await Assert.That(result.Outcome).IsEqualTo(AttachDiscIdOutcome.Conflict);
        await Assert.That(result.ExistingGlobalDiscId).IsEqualTo(OtherDiscId);

        // DB unchanged.
        await Assert.That(await ReloadDiscIdAsync(db)).IsEqualTo(OtherDiscId);

        // A pending change with a conflict note is filed for review (not applied).
        var change = await db.Set<EditSuggestionChange>().FirstAsync();
        await Assert.That(change.Status).IsEqualTo(EditSuggestionChangeStatus.Pending);
        await Assert.That(change.ConflictReason).IsNotNull();
    }

    [Test]
    public async Task AttachAsync_NoContentHashMatch_ReturnsNotFound()
    {
        using var db = ChangeTestSeed.CreateDbContext();
        var seed = ChangeTestSeed.Seed(db);
        seed.Disc.ContentHash = ContentHash;
        await db.SaveChangesAsync();

        var result = await CreateService(db).AttachAsync("user-1", "FFFF0000FFFF0000FFFF0000FFFF0000", DiscId, target: null);

        await Assert.That(result.Outcome).IsEqualTo(AttachDiscIdOutcome.NotFound);
        await Assert.That(await ReloadDiscIdAsync(db)).IsNull();
    }

    [Test]
    public async Task AttachAsync_CtaTargetWithMismatchedContentHash_ReturnsMismatch()
    {
        using var db = ChangeTestSeed.CreateDbContext();
        var seed = ChangeTestSeed.Seed(db);
        seed.Disc.ContentHash = ContentHash;
        await db.SaveChangesAsync();

        // Correct identity, but the inserted disc's content-hash does not match -> Mismatch (wrong disc).
        var target = new DiscTargetIdentity(ChangeTestSeed.MediaItemSlug, null, ChangeTestSeed.ReleaseSlug, ChangeTestSeed.DiscSlug, ChangeTestSeed.DiscIndex);
        var result = await CreateService(db).AttachAsync("user-1", "0000FFFF0000FFFF0000FFFF0000FFFF", DiscId, target);

        await Assert.That(result.Outcome).IsEqualTo(AttachDiscIdOutcome.Mismatch);
        await Assert.That(await ReloadDiscIdAsync(db)).IsNull();
        await Assert.That(await db.Set<EditSuggestionChange>().AnyAsync()).IsFalse();
    }

    [Test]
    public async Task AttachAsync_CtaTargetIdentityNotFound_ReturnsNotFound()
    {
        using var db = ChangeTestSeed.CreateDbContext();
        var seed = ChangeTestSeed.Seed(db);
        seed.Disc.ContentHash = ContentHash;
        await db.SaveChangesAsync();

        // Identity points at a disc that doesn't exist -> NotFound (not Mismatch).
        var target = new DiscTargetIdentity(ChangeTestSeed.MediaItemSlug, null, ChangeTestSeed.ReleaseSlug, "no-such-disc", 99);
        var result = await CreateService(db).AttachAsync("user-1", ContentHash, DiscId, target);

        await Assert.That(result.Outcome).IsEqualTo(AttachDiscIdOutcome.NotFound);
        await Assert.That(await ReloadDiscIdAsync(db)).IsNull();
    }

    [Test]
    public async Task AttachAsync_CtaTargetWithMatchingContentHash_Applies()
    {
        using var db = ChangeTestSeed.CreateDbContext();
        var seed = ChangeTestSeed.Seed(db);
        seed.Disc.ContentHash = ContentHash;
        await db.SaveChangesAsync();

        var target = new DiscTargetIdentity(ChangeTestSeed.MediaItemSlug, null, ChangeTestSeed.ReleaseSlug, ChangeTestSeed.DiscSlug, ChangeTestSeed.DiscIndex);
        var result = await CreateService(db).AttachAsync("user-1", ContentHash, DiscId, target);

        await Assert.That(result.Outcome).IsEqualTo(AttachDiscIdOutcome.Applied);
        await Assert.That(await ReloadDiscIdAsync(db)).IsEqualTo(DiscId);
    }

    [Test]
    public async Task AttachAsync_CtaTargetMismatch_ButInsertedDiscMatchesAnother_AppliesToThatDisc()
    {
        using var db = ChangeTestSeed.CreateDbContext();
        var seed = ChangeTestSeed.Seed(db);
        seed.Disc.ContentHash = ContentHash;

        // A second disc in the same release that the inserted disc actually matches (and still needs an id).
        const string otherHash = "9999888877776666555544443333EEEE";
        var otherDisc = new Disc { Slug = "disc-two", Index = 1, Name = "Disc Two", Format = "Blu-ray", ContentHash = otherHash };
        seed.Release.Discs.Add(new ReleaseDisc { Slug = "disc-two", Index = 1, Name = "Disc Two", Disc = otherDisc });
        await db.SaveChangesAsync();

        // Target is disc-one (the CTA), but the inserted disc's hash matches disc-two.
        var target = new DiscTargetIdentity(ChangeTestSeed.MediaItemSlug, null, ChangeTestSeed.ReleaseSlug, ChangeTestSeed.DiscSlug, ChangeTestSeed.DiscIndex);
        var result = await CreateService(db).AttachAsync("user-1", otherHash, DiscId, target);

        await Assert.That(result.Outcome).IsEqualTo(AttachDiscIdOutcome.Applied);
        await Assert.That(result.MatchedDifferentDisc).IsTrue();
        await Assert.That(result.DiscSlug).IsEqualTo("disc-two");

        // disc-two received the id; the CTA target disc-one is untouched.
        var applied = await db.Set<ReleaseDisc>().Include(x => x.Disc).FirstAsync(x => x.Slug == "disc-two");
        await Assert.That(applied.Disc!.GlobalDiscId).IsEqualTo(DiscId);
        await Assert.That(await ReloadDiscIdAsync(db)).IsNull();
    }
}
