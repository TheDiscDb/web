namespace TheDiscDb.UnitTests.Data.Changes.DiscItemFields;

using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using TheDiscDb.Data.Changes;
using TheDiscDb.Data.Changes.DiscItemFields;
using TheDiscDb.InputModels;

public class DiscItemAddDeleteTests
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private sealed record TestApplyContext(string ApprovingUserId, int SuggestionId, int ChangeId, string? OriginalSnapshotJson = null) : IChangeApplyContext;

    private const int FreeTitleIndex = 9;

    private static DiscItemFieldsDetails MakeAddProposed(int titleIndex, string? itemTitle = "New Disc Item")
        => new(
            MediaItemSlug: ChangeTestSeed.MediaItemSlug,
            BoxsetSlug: null,
            ReleaseSlug: ChangeTestSeed.ReleaseSlug,
            DiscSlug: ChangeTestSeed.DiscSlug,
            TitleIndex: titleIndex,
            Comment: "A comment",
            SourceFile: "00099.mpls",
            SegmentMap: null,
            Duration: "1:00:00",
            HasItem: true,
            ItemTitle: itemTitle,
            ItemType: "movie",
            ItemDescription: null,
            ItemSeason: null,
            ItemEpisode: null);

    private static DiscItemDeleteDetails MakeDeleteDetails(int titleIndex = ChangeTestSeed.TitleIndex)
        => new(
            MediaItemSlug: ChangeTestSeed.MediaItemSlug,
            BoxsetSlug: null,
            ReleaseSlug: ChangeTestSeed.ReleaseSlug,
            DiscSlug: ChangeTestSeed.DiscSlug,
            TitleIndex: titleIndex);

    // ---- Add ----

    [Test]
    public async Task Add_ValidateAsync_ReturnsOk_WhenIndexAvailable()
    {
        using var db = ChangeTestSeed.CreateDbContext();
        ChangeTestSeed.Seed(db);

        var change = new DiscItemAdd(MakeAddProposed(FreeTitleIndex));
        var result = await change.ValidateAsync(db, originalSnapshotJson: null, CancellationToken.None);

        await Assert.That(result.IsConflict).IsFalse();
        await Assert.That(result.IsNoOp).IsFalse();
    }

    [Test]
    public async Task Add_ApplyAsync_AddsTitleWithLinkedItem()
    {
        using var db = ChangeTestSeed.CreateDbContext();
        ChangeTestSeed.Seed(db);

        var change = new DiscItemAdd(MakeAddProposed(FreeTitleIndex, "Bonus Feature"));
        await change.ApplyAsync(db, new TestApplyContext("admin", 1, 1), CancellationToken.None);
        await db.SaveChangesAsync();

        var added = await db.Set<Title>().Include(t => t.Item).FirstOrDefaultAsync(t => t.Index == FreeTitleIndex);
        await Assert.That(added).IsNotNull();
        await Assert.That(added!.Item).IsNotNull();
        await Assert.That(added.Item!.Title).IsEqualTo("Bonus Feature");
    }

    [Test]
    public async Task Add_ApplyAsync_Throws_WhenIndexAlreadyTaken()
    {
        using var db = ChangeTestSeed.CreateDbContext();
        ChangeTestSeed.Seed(db);

        var change = new DiscItemAdd(MakeAddProposed(ChangeTestSeed.TitleIndex));

        await Assert.That(async () =>
        {
            await change.ApplyAsync(db, new TestApplyContext("admin", 1, 1), CancellationToken.None);
            await db.SaveChangesAsync();
        }).Throws<ChangeApplyConflictException>();
    }

    [Test]
    public async Task Add_ValidateAsync_ReturnsConflict_WhenParentDiscMissing()
    {
        using var db = ChangeTestSeed.CreateDbContext();
        ChangeTestSeed.Seed(db);

        var change = new DiscItemAdd(MakeAddProposed(FreeTitleIndex) with { DiscSlug = "no-such-disc" });
        var result = await change.ValidateAsync(db, originalSnapshotJson: null, CancellationToken.None);

        await Assert.That(result.IsConflict).IsTrue();
    }

    // ---- Delete ----

    [Test]
    public async Task Delete_ApplyAsync_RemovesTitle()
    {
        using var db = ChangeTestSeed.CreateDbContext();
        ChangeTestSeed.Seed(db);
        var snapshot = JsonSerializer.Serialize(MakeDeleteDetails(), JsonOptions);

        var change = new DiscItemDelete(MakeDeleteDetails());
        await change.ApplyAsync(db, new TestApplyContext("admin", 1, 1, snapshot), CancellationToken.None);
        await db.SaveChangesAsync();

        var stillThere = await db.Set<Title>().FirstOrDefaultAsync(t => t.Index == ChangeTestSeed.TitleIndex);
        await Assert.That(stillThere).IsNull();
    }

    [Test]
    public async Task Delete_ValidateAsync_ReturnsConflict_WhenTitleMissing()
    {
        using var db = ChangeTestSeed.CreateDbContext();
        ChangeTestSeed.Seed(db);
        var snapshot = JsonSerializer.Serialize(MakeDeleteDetails(99), JsonOptions);

        var change = new DiscItemDelete(MakeDeleteDetails(99));
        var result = await change.ValidateAsync(db, snapshot, CancellationToken.None);

        await Assert.That(result.IsConflict).IsTrue();
    }
}
