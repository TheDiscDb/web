namespace TheDiscDb.UnitTests.Data.Changes.DiscFields;

using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using TheDiscDb.Data.Changes;
using TheDiscDb.Data.Changes.DiscFields;

public class DiscFieldsUpdateTests
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private static DiscFieldsDetails MakeProposed(string? name = "Original Disc Name", string? format = "Blu-ray", string? discSlug = ChangeTestSeed.DiscSlug, int discIndex = ChangeTestSeed.DiscIndex)
        => new(
            MediaItemSlug: ChangeTestSeed.MediaItemSlug,
            BoxsetSlug: null,
            ReleaseSlug: ChangeTestSeed.ReleaseSlug,
            DiscSlug: discSlug,
            DiscIndex: discIndex,
            Name: name,
            Format: format);

    private sealed record TestApplyContext(string ApprovingUserId, int SuggestionId, int ChangeId, string? OriginalSnapshotJson = null) : IChangeApplyContext;

    [Test]
    public async Task TargetEntityKey_UsesDiscSlugWhenPresent()
    {
        var details = MakeProposed();
        await Assert.That(details.TargetEntityKey).IsEqualTo($"{ChangeTestSeed.MediaItemSlug}/{ChangeTestSeed.ReleaseSlug}/{ChangeTestSeed.DiscSlug}");
    }

    [Test]
    public async Task TargetEntityKey_FallsBackToIndexPrefix_WhenDiscSlugMissing()
    {
        var details = MakeProposed(discSlug: null);
        await Assert.That(details.TargetEntityKey).EndsWith("/i" + ChangeTestSeed.DiscIndex);
    }

    [Test]
    public async Task ValidateAsync_ReturnsConflict_WhenDiscDoesNotResolve()
    {
        using var db = ChangeTestSeed.CreateDbContext();
        ChangeTestSeed.Seed(db);

        var change = new DiscFieldsUpdate(MakeProposed(discSlug: "missing", discIndex: 99));

        var result = await change.ValidateAsync(db, originalSnapshotJson: null, CancellationToken.None);

        await Assert.That(result.IsConflict).IsTrue();
    }

    [Test]
    public async Task ValidateAsync_ReturnsConflict_WhenNeitherParentSlugSupplied()
    {
        using var db = ChangeTestSeed.CreateDbContext();
        ChangeTestSeed.Seed(db);

        var change = new DiscFieldsUpdate(MakeProposed() with { MediaItemSlug = null, BoxsetSlug = null });

        var result = await change.ValidateAsync(db, originalSnapshotJson: null, CancellationToken.None);

        await Assert.That(result.IsConflict).IsTrue();
    }

    [Test]
    public async Task ValidateAsync_ReturnsConflict_WhenSnapshotDrifts()
    {
        using var db = ChangeTestSeed.CreateDbContext();
        var seed = ChangeTestSeed.Seed(db);

        var staleSnapshot = JsonSerializer.Serialize(
            DiscFieldsUpdate.SnapshotFrom(seed.Disc, ChangeTestSeed.MediaItemSlug, null, ChangeTestSeed.ReleaseSlug) with { Name = "Older Disc Name" },
            JsonOptions);

        var change = new DiscFieldsUpdate(MakeProposed(name: "Proposed"));

        var result = await change.ValidateAsync(db, staleSnapshot, CancellationToken.None);

        await Assert.That(result.IsConflict).IsTrue();
        await Assert.That(result.ConflictReason).Contains("Name");
    }

    [Test]
    public async Task ApplyAsync_UpdatesEditableFields_AndDoesNotTouchIdentity()
    {
        using var db = ChangeTestSeed.CreateDbContext();
        var seed = ChangeTestSeed.Seed(db);
        var snapshot = JsonSerializer.Serialize(
            DiscFieldsUpdate.SnapshotFrom(seed.Disc, ChangeTestSeed.MediaItemSlug, null, ChangeTestSeed.ReleaseSlug),
            JsonOptions);

        var change = new DiscFieldsUpdate(MakeProposed(name: "Renamed Disc", format: "UHD Blu-ray"));
        await change.ApplyAsync(db, new TestApplyContext("admin", 1, 1, snapshot), CancellationToken.None);
        await db.SaveChangesAsync();

        var reloaded = await db.Set<TheDiscDb.InputModels.Disc>().FirstAsync(d => d.Slug == ChangeTestSeed.DiscSlug);
        await Assert.That(reloaded.Name).IsEqualTo("Renamed Disc");
        await Assert.That(reloaded.Format).IsEqualTo("UHD Blu-ray");
        await Assert.That(reloaded.Slug).IsEqualTo(ChangeTestSeed.DiscSlug);
        await Assert.That(reloaded.Index).IsEqualTo(ChangeTestSeed.DiscIndex);
    }

    [Test]
    public async Task ApplyAsync_OnlyWritesChangedFields()
    {
        using var db = ChangeTestSeed.CreateDbContext();
        var seed = ChangeTestSeed.Seed(db);
        var snapshotPayload = DiscFieldsUpdate.SnapshotFrom(seed.Disc, ChangeTestSeed.MediaItemSlug, null, ChangeTestSeed.ReleaseSlug);
        var snapshot = JsonSerializer.Serialize(snapshotPayload, JsonOptions);

        var change = new DiscFieldsUpdate(snapshotPayload with { Name = "Only Name Changed" });
        await change.ApplyAsync(db, new TestApplyContext("admin", 1, 1, snapshot), CancellationToken.None);
        await db.SaveChangesAsync();

        var reloaded = await db.Set<TheDiscDb.InputModels.Disc>().FirstAsync(d => d.Slug == ChangeTestSeed.DiscSlug);
        await Assert.That(reloaded.Name).IsEqualTo("Only Name Changed");
        await Assert.That(reloaded.Format).IsEqualTo("Blu-ray");
    }

    [Test]
    public async Task ApplyAsync_StillResolves_AfterDataRebuildShiftsIntIds()
    {
        // Genuine rebuild simulation: capture snapshot from one in-memory store,
        // then apply to a freshly-seeded second store. Int ids differ; the slug
        // composite is what we rely on to navigate to the right row.
        using var dbBefore = ChangeTestSeed.CreateDbContext();
        var seedBefore = ChangeTestSeed.Seed(dbBefore);
        var snapshot = JsonSerializer.Serialize(
            DiscFieldsUpdate.SnapshotFrom(seedBefore.Disc, ChangeTestSeed.MediaItemSlug, null, ChangeTestSeed.ReleaseSlug),
            JsonOptions);

        using var dbAfter = ChangeTestSeed.CreateDbContext();
        ChangeTestSeed.Seed(dbAfter);

        var change = new DiscFieldsUpdate(MakeProposed(name: "After-Rebuild Edit"));
        await change.ApplyAsync(dbAfter, new TestApplyContext("admin", 1, 1, snapshot), CancellationToken.None);
        await dbAfter.SaveChangesAsync();

        var reloaded = await dbAfter.Set<TheDiscDb.InputModels.Disc>().FirstAsync(d => d.Slug == ChangeTestSeed.DiscSlug);
        await Assert.That(reloaded.Name).IsEqualTo("After-Rebuild Edit");
    }

    [Test]
    public async Task ValidateAsync_ReturnsConflict_WhenBothParentSlugsSupplied()
    {
        // Identity contract: exactly one of MediaItemSlug / BoxsetSlug. Both-set
        // must surface as Conflict, not silently pick one.
        using var db = ChangeTestSeed.CreateDbContext();
        ChangeTestSeed.Seed(db);

        var change = new DiscFieldsUpdate(MakeProposed() with { BoxsetSlug = "some-boxset" });

        var result = await change.ValidateAsync(db, originalSnapshotJson: null, CancellationToken.None);

        await Assert.That(result.IsConflict).IsTrue();
    }

    [Test]
    public async Task ResolvesByIndex_WhenProposedDiscSlugIsEmpty()
    {
        using var db = ChangeTestSeed.CreateDbContext();
        var seed = ChangeTestSeed.Seed(db);

        seed.Disc.Slug = null;
        await db.SaveChangesAsync();

        var snapshot = JsonSerializer.Serialize(
            DiscFieldsUpdate.SnapshotFrom(seed.Disc, ChangeTestSeed.MediaItemSlug, null, ChangeTestSeed.ReleaseSlug),
            JsonOptions);

        var change = new DiscFieldsUpdate(MakeProposed(discSlug: null, name: "Edited"));
        await change.ApplyAsync(db, new TestApplyContext("admin", 1, 1, snapshot), CancellationToken.None);
        await db.SaveChangesAsync();

        var reloaded = await db.Set<TheDiscDb.InputModels.Disc>().FirstAsync(d => d.Index == ChangeTestSeed.DiscIndex);
        await Assert.That(reloaded.Name).IsEqualTo("Edited");
    }
}
