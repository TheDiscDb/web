namespace TheDiscDb.UnitTests.Data.Changes.Chapter;

using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using TheDiscDb.Data.Changes;
using TheDiscDb.Data.Changes.Chapter;

public class ChapterUpdateTests
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private static ChapterDetails MakeProposed(string? title = "Original Chapter Title", int chapterIndex = ChangeTestSeed.ChapterIndex)
        => new(
            MediaItemSlug: ChangeTestSeed.MediaItemSlug,
            BoxsetSlug: null,
            ReleaseSlug: ChangeTestSeed.ReleaseSlug,
            DiscSlug: ChangeTestSeed.DiscSlug,
            DiscIndex: ChangeTestSeed.DiscIndex,
            TitleIndex: ChangeTestSeed.TitleIndex,
            ChapterIndex: chapterIndex,
            Title: title);

    private sealed record TestApplyContext(string ApprovingUserId, int SuggestionId, int ChangeId, string? OriginalSnapshotJson = null) : IChangeApplyContext;

    [Test]
    public async Task TargetEntityKey_UsesCPrefixForChapterSegment()
    {
        var details = MakeProposed();
        await Assert.That(details.TargetEntityKey)
            .IsEqualTo($"{ChangeTestSeed.MediaItemSlug}/{ChangeTestSeed.ReleaseSlug}/{ChangeTestSeed.DiscSlug}/{ChangeTestSeed.TitleIndex}/c{ChangeTestSeed.ChapterIndex}");
    }

    [Test]
    public async Task ValidateAsync_ReturnsConflict_WhenChapterDoesNotResolve()
    {
        using var db = ChangeTestSeed.CreateDbContext();
        ChangeTestSeed.Seed(db);

        var change = new ChapterUpdate(MakeProposed(chapterIndex: 99));

        var result = await change.ValidateAsync(db, originalSnapshotJson: null, CancellationToken.None);

        await Assert.That(result.IsConflict).IsTrue();
    }

    [Test]
    public async Task ApplyAsync_UpdatesChapterTitle()
    {
        using var db = ChangeTestSeed.CreateDbContext();
        var seed = ChangeTestSeed.Seed(db);
        var snapshot = JsonSerializer.Serialize(
            ChapterUpdate.SnapshotFrom(seed.Chapter, ChangeTestSeed.MediaItemSlug, null, ChangeTestSeed.ReleaseSlug, ChangeTestSeed.DiscSlug, ChangeTestSeed.DiscIndex, ChangeTestSeed.TitleIndex),
            JsonOptions);

        var change = new ChapterUpdate(MakeProposed(title: "New Chapter Title"));
        await change.ApplyAsync(db, new TestApplyContext("admin", 1, 1, snapshot), CancellationToken.None);
        await db.SaveChangesAsync();

        var reloaded = await db.Set<TheDiscDb.InputModels.Chapter>().FirstAsync(c => c.Index == ChangeTestSeed.ChapterIndex);
        await Assert.That(reloaded.Title).IsEqualTo("New Chapter Title");
        await Assert.That(reloaded.Index).IsEqualTo(ChangeTestSeed.ChapterIndex);
    }

    [Test]
    public async Task ApplyAsync_StillResolves_AfterDataRebuildShiftsIntIds()
    {
        using var dbBefore = ChangeTestSeed.CreateDbContext();
        var seedBefore = ChangeTestSeed.Seed(dbBefore);
        var snapshot = JsonSerializer.Serialize(
            ChapterUpdate.SnapshotFrom(seedBefore.Chapter, ChangeTestSeed.MediaItemSlug, null, ChangeTestSeed.ReleaseSlug, ChangeTestSeed.DiscSlug, ChangeTestSeed.DiscIndex, ChangeTestSeed.TitleIndex),
            JsonOptions);

        using var dbAfter = ChangeTestSeed.CreateDbContext();
        ChangeTestSeed.Seed(dbAfter);

        var change = new ChapterUpdate(MakeProposed(title: "After-Rebuild Chapter"));
        await change.ApplyAsync(dbAfter, new TestApplyContext("admin", 1, 1, snapshot), CancellationToken.None);
        await dbAfter.SaveChangesAsync();

        var reloaded = await dbAfter.Set<TheDiscDb.InputModels.Chapter>().FirstAsync(c => c.Index == ChangeTestSeed.ChapterIndex);
        await Assert.That(reloaded.Title).IsEqualTo("After-Rebuild Chapter");
    }

    [Test]
    public async Task ValidateAsync_ReturnsConflict_WhenSnapshotDrifts()
    {
        using var db = ChangeTestSeed.CreateDbContext();
        var seed = ChangeTestSeed.Seed(db);
        var stale = JsonSerializer.Serialize(
            ChapterUpdate.SnapshotFrom(seed.Chapter, ChangeTestSeed.MediaItemSlug, null, ChangeTestSeed.ReleaseSlug, ChangeTestSeed.DiscSlug, ChangeTestSeed.DiscIndex, ChangeTestSeed.TitleIndex) with { Title = "Title nobody currently sees" },
            JsonOptions);

        var change = new ChapterUpdate(MakeProposed(title: "Proposed"));

        var result = await change.ValidateAsync(db, stale, CancellationToken.None);

        await Assert.That(result.IsConflict).IsTrue();
    }

    [Test]
    public async Task ValidateAsync_DetectsParentDiscIndexDrift_EvenWhenResolvedBySlug()
    {
        // Bug from review: when a snapshot captures (DiscSlug=disc-one,DiscIndex=0)
        // but the DB has been rebuilt with (DiscSlug=disc-one,DiscIndex=99),
        // resolution still matches by slug — but drift detection must flag the
        // index mismatch so the admin can decide what to do. Pre-fix, the
        // resolver built the "current" snapshot from proposed values and the
        // index drift was invisible.
        using var db = ChangeTestSeed.CreateDbContext();
        var seed = ChangeTestSeed.Seed(db);
        var snapshot = JsonSerializer.Serialize(
            ChapterUpdate.SnapshotFrom(seed.Chapter, ChangeTestSeed.MediaItemSlug, null, ChangeTestSeed.ReleaseSlug, ChangeTestSeed.DiscSlug, ChangeTestSeed.DiscIndex, ChangeTestSeed.TitleIndex),
            JsonOptions);

        // Simulate someone renumbering the discs in source data.
        seed.Disc.Index = 99;
        await db.SaveChangesAsync();

        var change = new ChapterUpdate(MakeProposed(title: "Proposed"));

        var result = await change.ValidateAsync(db, snapshot, CancellationToken.None);

        await Assert.That(result.IsConflict).IsTrue();
        await Assert.That(result.ConflictReason).Contains("identity");
    }

    [Test]
    public async Task ValidateAsync_ReturnsConflict_WhenBothParentSlugsSupplied()
    {
        using var db = ChangeTestSeed.CreateDbContext();
        ChangeTestSeed.Seed(db);

        var change = new ChapterUpdate(MakeProposed() with { BoxsetSlug = "some-boxset" });

        var result = await change.ValidateAsync(db, originalSnapshotJson: null, CancellationToken.None);

        await Assert.That(result.IsConflict).IsTrue();
    }
}
