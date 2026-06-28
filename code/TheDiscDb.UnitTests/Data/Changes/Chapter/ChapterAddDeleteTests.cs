namespace TheDiscDb.UnitTests.Data.Changes.Chapter;

using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using TheDiscDb.Data.Changes;
using TheDiscDb.Data.Changes.Chapter;
using EntityChapter = TheDiscDb.InputModels.Chapter;

public class ChapterAddDeleteTests
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private sealed record TestApplyContext(string ApprovingUserId, int SuggestionId, int ChangeId, string? OriginalSnapshotJson = null) : IChangeApplyContext;

    private const int FreeChapterIndex = 7;

    private static ChapterDetails MakeAddProposed(int chapterIndex, string? title = "New Chapter")
        => new(
            MediaItemSlug: ChangeTestSeed.MediaItemSlug,
            BoxsetSlug: null,
            ReleaseSlug: ChangeTestSeed.ReleaseSlug,
            DiscSlug: ChangeTestSeed.DiscSlug,
            TitleIndex: ChangeTestSeed.TitleIndex,
            ChapterIndex: chapterIndex,
            Title: title);

    private static ChapterDeleteDetails MakeDeleteDetails(int chapterIndex = ChangeTestSeed.ChapterIndex)
        => new(
            MediaItemSlug: ChangeTestSeed.MediaItemSlug,
            BoxsetSlug: null,
            ReleaseSlug: ChangeTestSeed.ReleaseSlug,
            DiscSlug: ChangeTestSeed.DiscSlug,
            TitleIndex: ChangeTestSeed.TitleIndex,
            ChapterIndex: chapterIndex);

    // ---- Add ----

    [Test]
    public async Task Add_ValidateAsync_ReturnsOk_WhenIndexAvailable()
    {
        using var db = ChangeTestSeed.CreateDbContext();
        ChangeTestSeed.Seed(db);

        var change = new ChapterAdd(MakeAddProposed(FreeChapterIndex));
        var result = await change.ValidateAsync(db, originalSnapshotJson: null, CancellationToken.None);

        await Assert.That(result.IsConflict).IsFalse();
        await Assert.That(result.IsNoOp).IsFalse();
    }

    [Test]
    public async Task Add_ApplyAsync_AddsChapterAtFreeIndex()
    {
        using var db = ChangeTestSeed.CreateDbContext();
        ChangeTestSeed.Seed(db);

        var change = new ChapterAdd(MakeAddProposed(FreeChapterIndex, "Brand New Chapter"));
        await change.ApplyAsync(db, new TestApplyContext("admin", 1, 1), CancellationToken.None);
        await db.SaveChangesAsync();

        var added = await db.Set<EntityChapter>().FirstOrDefaultAsync(c => c.Index == FreeChapterIndex);
        await Assert.That(added).IsNotNull();
        await Assert.That(added!.Title).IsEqualTo("Brand New Chapter");
    }

    [Test]
    public async Task Add_ApplyAsync_Throws_WhenIndexAlreadyTaken()
    {
        using var db = ChangeTestSeed.CreateDbContext();
        ChangeTestSeed.Seed(db);

        // ChangeTestSeed.ChapterIndex already exists.
        var change = new ChapterAdd(MakeAddProposed(ChangeTestSeed.ChapterIndex));

        await Assert.That(async () =>
        {
            await change.ApplyAsync(db, new TestApplyContext("admin", 1, 1), CancellationToken.None);
            await db.SaveChangesAsync();
        }).Throws<ChangeApplyConflictException>();
    }

    [Test]
    public async Task Add_ValidateAsync_ReturnsConflict_WhenParentItemMissing()
    {
        using var db = ChangeTestSeed.CreateDbContext();
        ChangeTestSeed.Seed(db);

        // No title at index 99 → parent cannot be resolved.
        var change = new ChapterAdd(MakeAddProposed(FreeChapterIndex) with { TitleIndex = 99 });
        var result = await change.ValidateAsync(db, originalSnapshotJson: null, CancellationToken.None);

        await Assert.That(result.IsConflict).IsTrue();
    }

    // ---- Delete ----

    [Test]
    public async Task Delete_ApplyAsync_RemovesChapter()
    {
        using var db = ChangeTestSeed.CreateDbContext();
        ChangeTestSeed.Seed(db);
        var snapshot = JsonSerializer.Serialize(MakeDeleteDetails(), JsonOptions);

        var change = new ChapterDelete(MakeDeleteDetails());
        await change.ApplyAsync(db, new TestApplyContext("admin", 1, 1, snapshot), CancellationToken.None);
        await db.SaveChangesAsync();

        var stillThere = await db.Set<EntityChapter>().FirstOrDefaultAsync(c => c.Index == ChangeTestSeed.ChapterIndex);
        await Assert.That(stillThere).IsNull();
    }

    [Test]
    public async Task Delete_ValidateAsync_ReturnsConflict_WhenChapterMissing()
    {
        using var db = ChangeTestSeed.CreateDbContext();
        ChangeTestSeed.Seed(db);
        var snapshot = JsonSerializer.Serialize(MakeDeleteDetails(99), JsonOptions);

        var change = new ChapterDelete(MakeDeleteDetails(99));
        var result = await change.ValidateAsync(db, snapshot, CancellationToken.None);

        await Assert.That(result.IsConflict).IsTrue();
    }
}
