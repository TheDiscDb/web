namespace TheDiscDb.UnitTests.Data.Changes.Track;

using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using TheDiscDb.Data.Changes;
using TheDiscDb.Data.Changes.Track;
using EntityTrack = TheDiscDb.InputModels.Track;

public class TrackAddDeleteTests
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private sealed record TestApplyContext(string ApprovingUserId, int SuggestionId, int ChangeId, string? OriginalSnapshotJson = null) : IChangeApplyContext;

    private const int FreeTrackIndex = 8;

    private static TrackFieldsDetails MakeAddProposed(int trackIndex, string? name = "New Track")
        => new(
            MediaItemSlug: ChangeTestSeed.MediaItemSlug,
            BoxsetSlug: null,
            ReleaseSlug: ChangeTestSeed.ReleaseSlug,
            DiscSlug: ChangeTestSeed.DiscSlug,
            TitleIndex: ChangeTestSeed.TitleIndex,
            TrackIndex: trackIndex,
            Name: name,
            Type: "Audio",
            Resolution: null,
            AspectRatio: null,
            AudioType: "DTS-HD MA",
            LanguageCode: "eng",
            Language: "English",
            Description: null);

    private static TrackDeleteDetails MakeDeleteDetails(int trackIndex = ChangeTestSeed.TrackIndex)
        => new(
            MediaItemSlug: ChangeTestSeed.MediaItemSlug,
            BoxsetSlug: null,
            ReleaseSlug: ChangeTestSeed.ReleaseSlug,
            DiscSlug: ChangeTestSeed.DiscSlug,
            TitleIndex: ChangeTestSeed.TitleIndex,
            TrackIndex: trackIndex);

    // ---- Add ----

    [Test]
    public async Task Add_ValidateAsync_ReturnsOk_WhenIndexAvailable()
    {
        using var db = ChangeTestSeed.CreateDbContext();
        ChangeTestSeed.Seed(db);

        var change = new TrackAdd(MakeAddProposed(FreeTrackIndex));
        var result = await change.ValidateAsync(db, originalSnapshotJson: null, CancellationToken.None);

        await Assert.That(result.IsConflict).IsFalse();
        await Assert.That(result.IsNoOp).IsFalse();
    }

    [Test]
    public async Task Add_ApplyAsync_AddsTrackAtFreeIndex()
    {
        using var db = ChangeTestSeed.CreateDbContext();
        ChangeTestSeed.Seed(db);

        var change = new TrackAdd(MakeAddProposed(FreeTrackIndex, "Commentary"));
        await change.ApplyAsync(db, new TestApplyContext("admin", 1, 1), CancellationToken.None);
        await db.SaveChangesAsync();

        var added = await db.Set<EntityTrack>().FirstOrDefaultAsync(t => t.Index == FreeTrackIndex);
        await Assert.That(added).IsNotNull();
        await Assert.That(added!.Name).IsEqualTo("Commentary");
    }

    [Test]
    public async Task Add_ApplyAsync_Throws_WhenIndexAlreadyTaken()
    {
        using var db = ChangeTestSeed.CreateDbContext();
        ChangeTestSeed.Seed(db);

        var change = new TrackAdd(MakeAddProposed(ChangeTestSeed.TrackIndex));

        await Assert.That(async () =>
        {
            await change.ApplyAsync(db, new TestApplyContext("admin", 1, 1), CancellationToken.None);
            await db.SaveChangesAsync();
        }).Throws<ChangeApplyConflictException>();
    }

    [Test]
    public async Task Add_ValidateAsync_ReturnsConflict_WhenParentTitleMissing()
    {
        using var db = ChangeTestSeed.CreateDbContext();
        ChangeTestSeed.Seed(db);

        var change = new TrackAdd(MakeAddProposed(FreeTrackIndex) with { TitleIndex = 99 });
        var result = await change.ValidateAsync(db, originalSnapshotJson: null, CancellationToken.None);

        await Assert.That(result.IsConflict).IsTrue();
    }

    // ---- Delete ----

    [Test]
    public async Task Delete_ApplyAsync_RemovesTrack()
    {
        using var db = ChangeTestSeed.CreateDbContext();
        ChangeTestSeed.Seed(db);
        var snapshot = JsonSerializer.Serialize(MakeDeleteDetails(), JsonOptions);

        var change = new TrackDelete(MakeDeleteDetails());
        await change.ApplyAsync(db, new TestApplyContext("admin", 1, 1, snapshot), CancellationToken.None);
        await db.SaveChangesAsync();

        var stillThere = await db.Set<EntityTrack>().FirstOrDefaultAsync(t => t.Index == ChangeTestSeed.TrackIndex);
        await Assert.That(stillThere).IsNull();
    }

    [Test]
    public async Task Delete_ValidateAsync_ReturnsConflict_WhenTrackMissing()
    {
        using var db = ChangeTestSeed.CreateDbContext();
        ChangeTestSeed.Seed(db);
        var snapshot = JsonSerializer.Serialize(MakeDeleteDetails(99), JsonOptions);

        var change = new TrackDelete(MakeDeleteDetails(99));
        var result = await change.ValidateAsync(db, snapshot, CancellationToken.None);

        await Assert.That(result.IsConflict).IsTrue();
    }
}
