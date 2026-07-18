namespace TheDiscDb.UnitTests.Data.Changes.DiscFields;

using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using TheDiscDb.Data.Changes;
using TheDiscDb.Data.Changes.DiscFields;

public class DiscFieldsUpdateTests
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private static DiscFieldsDetails MakeProposed(string? name = "Original Disc Name", string? format = "Blu-ray", string? discSlug = ChangeTestSeed.DiscSlug, int discIndex = ChangeTestSeed.DiscIndex, string? contentHash = null, string? globalDiscId = null)
        => new(
            MediaItemSlug: ChangeTestSeed.MediaItemSlug,
            BoxsetSlug: null,
            ReleaseSlug: ChangeTestSeed.ReleaseSlug,
            DiscSlug: discSlug,
            DiscIndex: discIndex,
            Name: name,
            Format: format,
            ContentHash: contentHash,
            GlobalDiscId: globalDiscId);

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

        var reloaded = await db.Set<TheDiscDb.InputModels.ReleaseDisc>().FirstAsync(d => d.Slug == ChangeTestSeed.DiscSlug);
        await Assert.That(reloaded.Name).IsEqualTo("Renamed Disc");
        await Assert.That(reloaded.Format).IsEqualTo("UHD Blu-ray");
        await Assert.That(reloaded.Slug).IsEqualTo(ChangeTestSeed.DiscSlug);
        await Assert.That(reloaded.Index).IsEqualTo(ChangeTestSeed.DiscIndex);
    }

    [Test]
    public async Task ApplyAsync_ClearsIsPartial_WhenProposedFalse()
    {
        using var db = ChangeTestSeed.CreateDbContext();
        var seed = ChangeTestSeed.Seed(db);

        // Mark the canonical disc partial, then propose clearing it (the manual "resolve" action).
        seed.Disc.IsPartial = true;
        await db.SaveChangesAsync();

        var snapshotPayload = DiscFieldsUpdate.SnapshotFrom(seed.Disc, ChangeTestSeed.MediaItemSlug, null, ChangeTestSeed.ReleaseSlug);
        await Assert.That(snapshotPayload.IsPartial).IsTrue();
        var snapshot = JsonSerializer.Serialize(snapshotPayload, JsonOptions);

        var change = new DiscFieldsUpdate(snapshotPayload with { IsPartial = false });
        await change.ApplyAsync(db, new TestApplyContext("admin", 1, 1, snapshot), CancellationToken.None);
        await db.SaveChangesAsync();

        var reloaded = await db.Set<TheDiscDb.InputModels.Disc>().FirstAsync(d => d.Id == seed.Disc.Id);
        await Assert.That(reloaded.IsPartial).IsFalse();
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

        var reloaded = await db.Set<TheDiscDb.InputModels.ReleaseDisc>().FirstAsync(d => d.Slug == ChangeTestSeed.DiscSlug);
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

        var reloaded = await dbAfter.Set<TheDiscDb.InputModels.ReleaseDisc>().FirstAsync(d => d.Slug == ChangeTestSeed.DiscSlug);
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
        seed.ReleaseDisc.Slug = null;
        await db.SaveChangesAsync();

        var snapshot = JsonSerializer.Serialize(
            DiscFieldsUpdate.SnapshotFrom(seed.Disc, ChangeTestSeed.MediaItemSlug, null, ChangeTestSeed.ReleaseSlug),
            JsonOptions);

        var change = new DiscFieldsUpdate(MakeProposed(discSlug: null, name: "Edited"));
        await change.ApplyAsync(db, new TestApplyContext("admin", 1, 1, snapshot), CancellationToken.None);
        await db.SaveChangesAsync();

        var reloaded = await db.Set<TheDiscDb.InputModels.ReleaseDisc>().FirstAsync(d => d.Index == ChangeTestSeed.DiscIndex);
        await Assert.That(reloaded.Name).IsEqualTo("Edited");
    }

    [Test]
    public async Task ApplyAsync_AddsContentHash_WhenDiscHasNone()
    {
        using var db = ChangeTestSeed.CreateDbContext();
        var seed = ChangeTestSeed.Seed(db);
        var snapshot = JsonSerializer.Serialize(
            DiscFieldsUpdate.SnapshotFrom(seed.Disc, ChangeTestSeed.MediaItemSlug, null, ChangeTestSeed.ReleaseSlug),
            JsonOptions);

        const string newHash = "F748A26D2BF1FEBD491EFA490B9AC6ED";
        var change = new DiscFieldsUpdate(MakeProposed(contentHash: newHash));
        await change.ApplyAsync(db, new TestApplyContext("admin", 1, 1, snapshot), CancellationToken.None);
        await db.SaveChangesAsync();

        var reloaded = await db.Set<TheDiscDb.InputModels.ReleaseDisc>().FirstAsync(d => d.Slug == ChangeTestSeed.DiscSlug);
        await Assert.That(reloaded.ContentHash).IsEqualTo(newHash);
    }

    [Test]
    public async Task ValidateAsync_ReturnsConflict_WhenContentHashCollidesWithAnotherDisc()
    {
        using var db = ChangeTestSeed.CreateDbContext();
        var seed = ChangeTestSeed.Seed(db);

        // Another disc already owns this (Format, ContentHash) — the unique key would be violated.
        const string collidingHash = "F748A26D2BF1FEBD491EFA490B9AC6ED";
        var other = new TheDiscDb.InputModels.Disc { Slug = "disc-two", Index = 1, Name = "Disc Two", Format = "Blu-ray", ContentHash = collidingHash };
        seed.Release.Discs.Add(new TheDiscDb.InputModels.ReleaseDisc { Slug = "disc-two", Index = 1, Name = "Disc Two", Disc = other });
        await db.SaveChangesAsync();

        var snapshot = JsonSerializer.Serialize(
            DiscFieldsUpdate.SnapshotFrom(seed.Disc, ChangeTestSeed.MediaItemSlug, null, ChangeTestSeed.ReleaseSlug),
            JsonOptions);
        var change = new DiscFieldsUpdate(MakeProposed(contentHash: collidingHash));

        var result = await change.ValidateAsync(db, snapshot, CancellationToken.None);

        await Assert.That(result.IsConflict).IsTrue();
        await Assert.That(result.ConflictReason).Contains("already assigned");
    }

    [Test]
    public async Task ValidateAsync_NotConflict_WhenContentHashIsUnique()
    {
        using var db = ChangeTestSeed.CreateDbContext();
        var seed = ChangeTestSeed.Seed(db);
        await db.SaveChangesAsync();

        var snapshot = JsonSerializer.Serialize(
            DiscFieldsUpdate.SnapshotFrom(seed.Disc, ChangeTestSeed.MediaItemSlug, null, ChangeTestSeed.ReleaseSlug),
            JsonOptions);
        var change = new DiscFieldsUpdate(MakeProposed(contentHash: "1111222233334444AAAABBBBCCCCDDDD"));

        var result = await change.ValidateAsync(db, snapshot, CancellationToken.None);

        await Assert.That(result.IsConflict).IsFalse();
    }

    [Test]
    public async Task ApplyAsync_DoesNotOverwriteContentHash_WhenAlreadyPresent()
    {
        using var db = ChangeTestSeed.CreateDbContext();
        var seed = ChangeTestSeed.Seed(db);
        const string existingHash = "AAAA1111BBBB2222CCCC3333DDDD4444";
        seed.Disc.ContentHash = existingHash;
        await db.SaveChangesAsync();

        var snapshot = JsonSerializer.Serialize(
            DiscFieldsUpdate.SnapshotFrom(seed.Disc, ChangeTestSeed.MediaItemSlug, null, ChangeTestSeed.ReleaseSlug),
            JsonOptions);

        var change = new DiscFieldsUpdate(MakeProposed(contentHash: "9999888877776666555544443333EEEE"));
        await change.ApplyAsync(db, new TestApplyContext("admin", 1, 1, snapshot), CancellationToken.None);
        await db.SaveChangesAsync();

        var reloaded = await db.Set<TheDiscDb.InputModels.ReleaseDisc>().FirstAsync(d => d.Slug == ChangeTestSeed.DiscSlug);
        await Assert.That(reloaded.ContentHash).IsEqualTo(existingHash);
    }

    [Test]
    public async Task ValidateAsync_ReturnsConflict_WhenContentHashDrifts()
    {
        using var db = ChangeTestSeed.CreateDbContext();
        var seed = ChangeTestSeed.Seed(db);
        seed.Disc.ContentHash = "AAAA1111BBBB2222CCCC3333DDDD4444";
        await db.SaveChangesAsync();

        // Snapshot taken when the disc had no hash; the DB now has one -> drift.
        var staleSnapshot = JsonSerializer.Serialize(
            DiscFieldsUpdate.SnapshotFrom(seed.Disc, ChangeTestSeed.MediaItemSlug, null, ChangeTestSeed.ReleaseSlug) with { ContentHash = null },
            JsonOptions);

        var change = new DiscFieldsUpdate(MakeProposed(contentHash: "9999888877776666555544443333EEEE"));

        var result = await change.ValidateAsync(db, staleSnapshot, CancellationToken.None);

        await Assert.That(result.IsConflict).IsTrue();
        await Assert.That(result.ConflictReason).Contains("ContentHash");
    }

    [Test]
    public async Task ValidateAsync_NotConflict_WhenFormatAndHashDrift_ButOnlyNameProposed()
    {
        // Regression: a data rebuild may add Format/ContentHash to discs that had
        // neither when the suggestion was submitted. A name-only suggestion must not
        // be blocked by drift in fields it never touched.
        using var db = ChangeTestSeed.CreateDbContext();
        var seed = ChangeTestSeed.Seed(db);

        // Snapshot captured before rebuild: no Format or ContentHash.
        var snapshot = DiscFieldsUpdate.SnapshotFrom(seed.Disc, ChangeTestSeed.MediaItemSlug, null, ChangeTestSeed.ReleaseSlug)
            with { Format = null, ContentHash = null };
        var snapshotJson = JsonSerializer.Serialize(snapshot, JsonOptions);

        // Simulate rebuild: disc now has Format and ContentHash.
        seed.Disc.Format = "UHD Blu-ray";
        seed.Disc.ContentHash = "BBBB2222CCCC3333DDDD4444EEEE5555";
        seed.ReleaseDisc.Format = "UHD Blu-ray";
        seed.ReleaseDisc.ContentHash = "BBBB2222CCCC3333DDDD4444EEEE5555";
        await db.SaveChangesAsync();

        // The suggestion only proposes a name change; Format/ContentHash are unchanged from snapshot.
        var change = new DiscFieldsUpdate(snapshot with { Name = "Renamed Disc" });

        var result = await change.ValidateAsync(db, snapshotJson, CancellationToken.None);

        await Assert.That(result.IsConflict).IsFalse();
    }

    [Test]
    public async Task ValidateAsync_NotConflict_WhenFormatDrifts_ButOnlyNameProposed()
    {
        // Format changed independently (e.g. corrected by another edit); a name-only
        // suggestion should still apply.
        using var db = ChangeTestSeed.CreateDbContext();
        var seed = ChangeTestSeed.Seed(db);

        var snapshot = DiscFieldsUpdate.SnapshotFrom(seed.Disc, ChangeTestSeed.MediaItemSlug, null, ChangeTestSeed.ReleaseSlug);
        var snapshotJson = JsonSerializer.Serialize(snapshot, JsonOptions);

        // Someone changed the format after the snapshot was taken.
        seed.ReleaseDisc.Format = "UHD Blu-ray";
        await db.SaveChangesAsync();

        // Name-only proposal.
        var change = new DiscFieldsUpdate(snapshot with { Name = "Better Title" });

        var result = await change.ValidateAsync(db, snapshotJson, CancellationToken.None);

        await Assert.That(result.IsConflict).IsFalse();
    }

    [Test]
    public async Task ApplyAsync_AddsGlobalDiscId_WhenDiscHasNone()
    {
        using var db = ChangeTestSeed.CreateDbContext();
        var seed = ChangeTestSeed.Seed(db);
        var snapshot = JsonSerializer.Serialize(
            DiscFieldsUpdate.SnapshotFrom(seed.Disc, ChangeTestSeed.MediaItemSlug, null, ChangeTestSeed.ReleaseSlug),
            JsonOptions);

        const string newDiscId = "A734E4BEE726B8943F2E8817E3956EFC5F786C8B";
        var change = new DiscFieldsUpdate(MakeProposed(globalDiscId: newDiscId));
        await change.ApplyAsync(db, new TestApplyContext("admin", 1, 1, snapshot), CancellationToken.None);
        await db.SaveChangesAsync();

        var reloaded = await db.Set<TheDiscDb.InputModels.ReleaseDisc>().FirstAsync(d => d.Slug == ChangeTestSeed.DiscSlug);
        await Assert.That(reloaded.GlobalDiscId).IsEqualTo(newDiscId);
    }

    [Test]
    public async Task ApplyAsync_DoesNotOverwriteGlobalDiscId_WhenAlreadyPresent()
    {
        using var db = ChangeTestSeed.CreateDbContext();
        var seed = ChangeTestSeed.Seed(db);
        const string existing = "A734E4BEE726B8943F2E8817E3956EFC5F786C8B";
        seed.Disc.GlobalDiscId = existing;
        await db.SaveChangesAsync();

        var snapshot = JsonSerializer.Serialize(
            DiscFieldsUpdate.SnapshotFrom(seed.Disc, ChangeTestSeed.MediaItemSlug, null, ChangeTestSeed.ReleaseSlug),
            JsonOptions);

        var change = new DiscFieldsUpdate(MakeProposed(globalDiscId: "91C2EB717C4323D8807C01BA79011A6B"));
        await change.ApplyAsync(db, new TestApplyContext("admin", 1, 1, snapshot), CancellationToken.None);
        await db.SaveChangesAsync();

        var reloaded = await db.Set<TheDiscDb.InputModels.ReleaseDisc>().FirstAsync(d => d.Slug == ChangeTestSeed.DiscSlug);
        await Assert.That(reloaded.GlobalDiscId).IsEqualTo(existing);
    }

    [Test]
    public async Task ValidateAsync_ReturnsConflict_WhenGlobalDiscIdDrifts()
    {
        using var db = ChangeTestSeed.CreateDbContext();
        var seed = ChangeTestSeed.Seed(db);
        seed.Disc.GlobalDiscId = "A734E4BEE726B8943F2E8817E3956EFC5F786C8B";
        await db.SaveChangesAsync();

        // Snapshot taken when the disc had no Disc ID; the DB now has one -> drift.
        var staleSnapshot = JsonSerializer.Serialize(
            DiscFieldsUpdate.SnapshotFrom(seed.Disc, ChangeTestSeed.MediaItemSlug, null, ChangeTestSeed.ReleaseSlug) with { GlobalDiscId = null },
            JsonOptions);

        var change = new DiscFieldsUpdate(MakeProposed(globalDiscId: "91C2EB717C4323D8807C01BA79011A6B"));

        var result = await change.ValidateAsync(db, staleSnapshot, CancellationToken.None);

        await Assert.That(result.IsConflict).IsTrue();
        await Assert.That(result.ConflictReason).Contains("GlobalDiscId");
    }
}
