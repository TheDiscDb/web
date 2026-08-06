namespace TheDiscDb.UnitTests.Data.Changes;

using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using TheDiscDb.Data.Changes;
using TheDiscDb.Data.Changes.DiscPartial;
using TheDiscDb.Data.Changes.ReleasePartial;

public class PartialUpdateTests
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private sealed record TestApplyContext(
        string ApprovingUserId,
        int SuggestionId,
        int ChangeId,
        string? OriginalSnapshotJson = null) : IChangeApplyContext;

    [Test]
    public async Task ReleasePartialUpdate_SetsAndClearsUsingNaturalKey()
    {
        using var db = ChangeTestSeed.CreateDbContext();
        var seed = ChangeTestSeed.Seed(db);
        var original = ReleasePartialUpdate.SnapshotFrom(
            seed.Release,
            ChangeTestSeed.MediaItemSlug,
            boxsetSlug: null);
        var partial = new PartialState
        {
            Type = PartialStateType.MissingDiscs,
            Reason = PartialStateReason.DiscMissing,
            Source = PartialStateSource.Declared,
            Note = "Bonus disc is missing.",
        };

        var set = new ReleasePartialUpdate(original with { Partial = partial });
        var originalJson = JsonSerializer.Serialize(original, JsonOptions);
        await set.ApplyAsync(db, new TestApplyContext("admin", 1, 1, originalJson), CancellationToken.None);
        await db.SaveChangesAsync();

        await Assert.That(seed.Release.Partial).IsEqualTo(partial);

        var current = ReleasePartialUpdate.SnapshotFrom(
            seed.Release,
            ChangeTestSeed.MediaItemSlug,
            boxsetSlug: null);
        var clear = new ReleasePartialUpdate(current with { Partial = null });
        await clear.ApplyAsync(
            db,
            new TestApplyContext("admin", 1, 2, JsonSerializer.Serialize(current, JsonOptions)),
            CancellationToken.None);
        await db.SaveChangesAsync();

        await Assert.That(seed.Release.Partial).IsNull();
        await Assert.That(clear.TargetEntityKey)
            .IsEqualTo($"{ChangeTestSeed.MediaItemSlug}/{ChangeTestSeed.ReleaseSlug}");
    }

    [Test]
    public async Task DiscPartialUpdate_SetsCanonicalDiscAndDetectsSnapshotConflict()
    {
        using var db = ChangeTestSeed.CreateDbContext();
        var seed = ChangeTestSeed.Seed(db);
        var original = DiscPartialUpdate.SnapshotFrom(
            seed.ReleaseDisc,
            ChangeTestSeed.MediaItemSlug,
            boxsetSlug: null,
            ChangeTestSeed.ReleaseSlug);
        var proposed = original with
        {
            Partial = new PartialState
            {
                Type = PartialStateType.PartiallyIdentified,
                Reason = PartialStateReason.ExtrasOutOfScope,
                Source = PartialStateSource.Declared,
            },
        };
        var change = new DiscPartialUpdate(proposed);
        var originalJson = JsonSerializer.Serialize(original, JsonOptions);

        await change.ApplyAsync(db, new TestApplyContext("admin", 1, 1, originalJson), CancellationToken.None);
        await db.SaveChangesAsync();

        await Assert.That(seed.Disc.Partial).IsEqualTo(proposed.Partial);
        await Assert.That(change.TargetEntityKey)
            .IsEqualTo($"{ChangeTestSeed.MediaItemSlug}/{ChangeTestSeed.ReleaseSlug}/{ChangeTestSeed.DiscSlug}");

        var staleChange = new DiscPartialUpdate(proposed with { Partial = null });
        var validation = await staleChange.ValidateAsync(db, originalJson, CancellationToken.None);
        await Assert.That(validation.IsConflict).IsTrue();
    }

    [Test]
    public async Task DiscPartialUpdate_RejectsReleaseOnlyPartialType()
    {
        using var db = ChangeTestSeed.CreateDbContext();
        var seed = ChangeTestSeed.Seed(db);
        var original = DiscPartialUpdate.SnapshotFrom(
            seed.ReleaseDisc,
            ChangeTestSeed.MediaItemSlug,
            boxsetSlug: null,
            ChangeTestSeed.ReleaseSlug);
        var change = new DiscPartialUpdate(original with
        {
            Partial = new PartialState
            {
                Type = PartialStateType.MissingDiscs,
                Reason = PartialStateReason.DiscMissing,
                Source = PartialStateSource.Declared,
            },
        });

        var validation = await change.ValidateAsync(
            db,
            JsonSerializer.Serialize(original, JsonOptions),
            CancellationToken.None);

        await Assert.That(validation.IsConflict).IsTrue();
    }
}
