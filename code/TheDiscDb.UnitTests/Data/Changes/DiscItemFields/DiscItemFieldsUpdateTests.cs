namespace TheDiscDb.UnitTests.Data.Changes.DiscItemFields;

using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using TheDiscDb.Data.Changes;
using TheDiscDb.Data.Changes.DiscItemFields;

public class DiscItemFieldsUpdateTests
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private static DiscItemFieldsDetails MakeProposed(
        string? comment = "Original comment",
        string? sourceFile = "00001.mpls",
        string? segmentMap = "1,2,3",
        string? duration = "2:13:45",
        bool hasItem = true,
        string? itemTitle = "Original Item Title",
        string? itemType = "movie",
        string? itemDescription = "Original description.",
        string? itemSeason = "1",
        string? itemEpisode = "3")
        => new(
            MediaItemSlug: ChangeTestSeed.MediaItemSlug,
            BoxsetSlug: null,
            ReleaseSlug: ChangeTestSeed.ReleaseSlug,
            DiscSlug: ChangeTestSeed.DiscSlug,
            TitleIndex: ChangeTestSeed.TitleIndex,
            Comment: comment,
            SourceFile: sourceFile,
            SegmentMap: segmentMap,
            Duration: duration,
            HasItem: hasItem,
            ItemTitle: itemTitle,
            ItemType: itemType,
            ItemDescription: itemDescription,
            ItemSeason: itemSeason,
            ItemEpisode: itemEpisode);

    private sealed record TestApplyContext(string ApprovingUserId, int SuggestionId, int ChangeId, string? OriginalSnapshotJson = null) : IChangeApplyContext;

    [Test]
    public async Task TargetEntityKey_ComposesParentReleaseDiscTitle()
    {
        var details = MakeProposed();
        await Assert.That(details.TargetEntityKey).IsEqualTo($"{ChangeTestSeed.MediaItemSlug}/{ChangeTestSeed.ReleaseSlug}/{ChangeTestSeed.DiscSlug}/{ChangeTestSeed.TitleIndex}");
    }

    [Test]
    public async Task ValidateAsync_ReturnsConflict_WhenTitleIndexDoesNotResolve()
    {
        using var db = ChangeTestSeed.CreateDbContext();
        ChangeTestSeed.Seed(db);

        var change = new DiscItemFieldsUpdate(MakeProposed() with { TitleIndex = 999 });

        var result = await change.ValidateAsync(db, originalSnapshotJson: null, CancellationToken.None);

        await Assert.That(result.IsConflict).IsTrue();
    }

    [Test]
    public async Task ApplyAsync_UpdatesTitleAndItemFields_LeavesOthersUntouched()
    {
        using var db = ChangeTestSeed.CreateDbContext();
        var seed = ChangeTestSeed.Seed(db);
        var snapshotPayload = DiscItemFieldsUpdate.SnapshotFrom(
            seed.Title, ChangeTestSeed.MediaItemSlug, null, ChangeTestSeed.ReleaseSlug, ChangeTestSeed.DiscSlug);
        var snapshot = JsonSerializer.Serialize(snapshotPayload, JsonOptions);

        var change = new DiscItemFieldsUpdate(snapshotPayload with { Comment = "Updated comment", ItemTitle = "Updated Item Title" });
        await change.ApplyAsync(db, new TestApplyContext("admin", 1, 1, snapshot), CancellationToken.None);
        await db.SaveChangesAsync();

        var reloaded = await db.Set<TheDiscDb.InputModels.Title>()
            .Include(t => t.Item)
            .FirstAsync(t => t.Index == ChangeTestSeed.TitleIndex);
        await Assert.That(reloaded.Comment).IsEqualTo("Updated comment");
        await Assert.That(reloaded.SourceFile).IsEqualTo("00001.mpls");
        await Assert.That(reloaded.Item!.Title).IsEqualTo("Updated Item Title");
        await Assert.That(reloaded.Item!.Type).IsEqualTo("movie");
    }

    [Test]
    public async Task ApplyAsync_CreatesItemReferenceLazily_WhenAbsentAndItemFieldsProposed()
    {
        using var db = ChangeTestSeed.CreateDbContext();
        var seed = ChangeTestSeed.Seed(db);

        seed.Title.Item = null;
        await db.SaveChangesAsync();

        var snapshotPayload = DiscItemFieldsUpdate.SnapshotFrom(
            seed.Title, ChangeTestSeed.MediaItemSlug, null, ChangeTestSeed.ReleaseSlug, ChangeTestSeed.DiscSlug);
        var snapshot = JsonSerializer.Serialize(snapshotPayload, JsonOptions);

        var change = new DiscItemFieldsUpdate(snapshotPayload with
        {
            ItemTitle = "Newly Attached Title",
            ItemType = "movie",
        });

        await change.ApplyAsync(db, new TestApplyContext("admin", 1, 1, snapshot), CancellationToken.None);
        await db.SaveChangesAsync();

        var reloaded = await db.Set<TheDiscDb.InputModels.Title>()
            .Include(t => t.Item)
            .FirstAsync(t => t.Index == ChangeTestSeed.TitleIndex);
        await Assert.That(reloaded.Item).IsNotNull();
        await Assert.That(reloaded.Item!.Title).IsEqualTo("Newly Attached Title");
    }

    [Test]
    public async Task ApplyAsync_StillResolves_AfterDataRebuildShiftsIntIds()
    {
        using var dbBefore = ChangeTestSeed.CreateDbContext();
        var seedBefore = ChangeTestSeed.Seed(dbBefore);
        var snapshot = JsonSerializer.Serialize(
            DiscItemFieldsUpdate.SnapshotFrom(seedBefore.Title, ChangeTestSeed.MediaItemSlug, null, ChangeTestSeed.ReleaseSlug, ChangeTestSeed.DiscSlug),
            JsonOptions);

        using var dbAfter = ChangeTestSeed.CreateDbContext();
        ChangeTestSeed.Seed(dbAfter);

        var change = new DiscItemFieldsUpdate(MakeProposed(comment: "After-rebuild edit"));
        await change.ApplyAsync(dbAfter, new TestApplyContext("admin", 1, 1, snapshot), CancellationToken.None);
        await dbAfter.SaveChangesAsync();

        var reloaded = await dbAfter.Set<TheDiscDb.InputModels.Title>().FirstAsync(t => t.Index == ChangeTestSeed.TitleIndex);
        await Assert.That(reloaded.Comment).IsEqualTo("After-rebuild edit");
    }

    [Test]
    public async Task ValidateAsync_ReturnsConflict_WhenDiscSlugIsMissing()
    {
        using var db = ChangeTestSeed.CreateDbContext();
        ChangeTestSeed.Seed(db);

        var change = new DiscItemFieldsUpdate(MakeProposed() with { DiscSlug = string.Empty });

        var result = await change.ValidateAsync(db, originalSnapshotJson: null, CancellationToken.None);

        await Assert.That(result.IsConflict).IsTrue();
    }

    [Test]
    public async Task ValidateAsync_NotConflict_WhenUnproposedItemFieldsDrift_DuringRebuild()
    {
        // Regression: a rebuild may add or correct SegmentMap/SourceFile after a comment-only
        // suggestion was submitted. The comment-only suggestion must not be blocked.
        using var db = ChangeTestSeed.CreateDbContext();
        var seed = ChangeTestSeed.Seed(db);

        var snapshotAtSubmit = DiscItemFieldsUpdate.SnapshotFrom(
            seed.Title, ChangeTestSeed.MediaItemSlug, null, ChangeTestSeed.ReleaseSlug, ChangeTestSeed.DiscSlug)
            with { SegmentMap = null };
        var snapshotJson = JsonSerializer.Serialize(snapshotAtSubmit, JsonOptions);

        // Rebuild corrects SegmentMap.
        seed.Title.SegmentMap = "1,2,3,4";
        await db.SaveChangesAsync();

        // Suggestion only proposes a Comment change; SegmentMap unchanged from snapshot.
        var change = new DiscItemFieldsUpdate(snapshotAtSubmit with { Comment = "Updated comment" });

        var result = await change.ValidateAsync(db, snapshotJson, CancellationToken.None);

        await Assert.That(result.IsConflict).IsFalse();
    }

    [Test]
    public async Task ValidateAsync_ReturnsConflict_WhenBothParentSlugsSupplied()
    {
        using var db = ChangeTestSeed.CreateDbContext();
        ChangeTestSeed.Seed(db);

        var change = new DiscItemFieldsUpdate(MakeProposed() with { BoxsetSlug = "some-boxset" });

        var result = await change.ValidateAsync(db, originalSnapshotJson: null, CancellationToken.None);

        await Assert.That(result.IsConflict).IsTrue();
    }
}
