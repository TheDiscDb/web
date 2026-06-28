namespace TheDiscDb.UnitTests.Data.Changes.Track;

using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using TheDiscDb.Data.Changes;
using TheDiscDb.Data.Changes.Track;

public class TrackFieldsUpdateTests
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private static TrackFieldsDetails MakeProposed(
        string? name = "Original Track Name",
        string? type = "Audio",
        string? resolution = null,
        string? aspectRatio = null,
        string? audioType = "DTS-HD MA",
        string? languageCode = "eng",
        string? language = "English",
        string? description = "Original track desc",
        int trackIndex = ChangeTestSeed.TrackIndex)
        => new(
            MediaItemSlug: ChangeTestSeed.MediaItemSlug,
            BoxsetSlug: null,
            ReleaseSlug: ChangeTestSeed.ReleaseSlug,
            DiscSlug: ChangeTestSeed.DiscSlug,
            TitleIndex: ChangeTestSeed.TitleIndex,
            TrackIndex: trackIndex,
            Name: name,
            Type: type,
            Resolution: resolution,
            AspectRatio: aspectRatio,
            AudioType: audioType,
            LanguageCode: languageCode,
            Language: language,
            Description: description);

    private sealed record TestApplyContext(string ApprovingUserId, int SuggestionId, int ChangeId, string? OriginalSnapshotJson = null) : IChangeApplyContext;

    [Test]
    public async Task TargetEntityKey_UsesTPrefixForTrackSegment()
    {
        var details = MakeProposed();
        await Assert.That(details.TargetEntityKey)
            .IsEqualTo($"{ChangeTestSeed.MediaItemSlug}/{ChangeTestSeed.ReleaseSlug}/{ChangeTestSeed.DiscSlug}/{ChangeTestSeed.TitleIndex}/t{ChangeTestSeed.TrackIndex}");
    }

    [Test]
    public async Task ValidateAsync_ReturnsConflict_WhenTrackDoesNotResolve()
    {
        using var db = ChangeTestSeed.CreateDbContext();
        ChangeTestSeed.Seed(db);

        var change = new TrackFieldsUpdate(MakeProposed(trackIndex: 99));

        var result = await change.ValidateAsync(db, originalSnapshotJson: null, CancellationToken.None);

        await Assert.That(result.IsConflict).IsTrue();
    }

    [Test]
    public async Task ApplyAsync_UpdatesAllAudioFields()
    {
        using var db = ChangeTestSeed.CreateDbContext();
        var seed = ChangeTestSeed.Seed(db);
        var snapshot = JsonSerializer.Serialize(
            TrackFieldsUpdate.SnapshotFrom(seed.Track, ChangeTestSeed.MediaItemSlug, null, ChangeTestSeed.ReleaseSlug, ChangeTestSeed.DiscSlug, ChangeTestSeed.TitleIndex),
            JsonOptions);

        var change = new TrackFieldsUpdate(MakeProposed(
            name: "Updated Name",
            audioType: "Dolby Atmos",
            languageCode: "deu",
            language: "German",
            description: "Updated desc"));
        await change.ApplyAsync(db, new TestApplyContext("admin", 1, 1, snapshot), CancellationToken.None);
        await db.SaveChangesAsync();

        var reloaded = await db.Set<TheDiscDb.InputModels.Track>().FirstAsync(t => t.Index == ChangeTestSeed.TrackIndex);
        await Assert.That(reloaded.Name).IsEqualTo("Updated Name");
        await Assert.That(reloaded.AudioType).IsEqualTo("Dolby Atmos");
        await Assert.That(reloaded.LanguageCode).IsEqualTo("deu");
        await Assert.That(reloaded.Language).IsEqualTo("German");
        await Assert.That(reloaded.Description).IsEqualTo("Updated desc");
        await Assert.That(reloaded.Index).IsEqualTo(ChangeTestSeed.TrackIndex);
    }

    [Test]
    public async Task ApplyAsync_AllowsRetypingTrack_AudioToSubtitle()
    {
        using var db = ChangeTestSeed.CreateDbContext();
        var seed = ChangeTestSeed.Seed(db);
        var snapshotPayload = TrackFieldsUpdate.SnapshotFrom(seed.Track, ChangeTestSeed.MediaItemSlug, null, ChangeTestSeed.ReleaseSlug, ChangeTestSeed.DiscSlug, ChangeTestSeed.TitleIndex);
        var snapshot = JsonSerializer.Serialize(snapshotPayload, JsonOptions);

        var change = new TrackFieldsUpdate(snapshotPayload with { Type = "Subtitle", AudioType = null });
        await change.ApplyAsync(db, new TestApplyContext("admin", 1, 1, snapshot), CancellationToken.None);
        await db.SaveChangesAsync();

        var reloaded = await db.Set<TheDiscDb.InputModels.Track>().FirstAsync(t => t.Index == ChangeTestSeed.TrackIndex);
        await Assert.That(reloaded.Type).IsEqualTo("Subtitle");
        await Assert.That(reloaded.AudioType).IsNull();
    }

    [Test]
    public async Task ApplyAsync_StillResolves_AfterDataRebuildShiftsIntIds()
    {
        using var dbBefore = ChangeTestSeed.CreateDbContext();
        var seedBefore = ChangeTestSeed.Seed(dbBefore);
        var snapshot = JsonSerializer.Serialize(
            TrackFieldsUpdate.SnapshotFrom(seedBefore.Track, ChangeTestSeed.MediaItemSlug, null, ChangeTestSeed.ReleaseSlug, ChangeTestSeed.DiscSlug, ChangeTestSeed.TitleIndex),
            JsonOptions);

        using var dbAfter = ChangeTestSeed.CreateDbContext();
        ChangeTestSeed.Seed(dbAfter);

        var change = new TrackFieldsUpdate(MakeProposed(name: "After-Rebuild Track"));
        await change.ApplyAsync(dbAfter, new TestApplyContext("admin", 1, 1, snapshot), CancellationToken.None);
        await dbAfter.SaveChangesAsync();

        var reloaded = await dbAfter.Set<TheDiscDb.InputModels.Track>().FirstAsync(t => t.Index == ChangeTestSeed.TrackIndex);
        await Assert.That(reloaded.Name).IsEqualTo("After-Rebuild Track");
    }

    [Test]
    public async Task ValidateAsync_ReturnsConflict_WhenDiscSlugIsMissing()
    {
        using var db = ChangeTestSeed.CreateDbContext();
        ChangeTestSeed.Seed(db);

        var change = new TrackFieldsUpdate(MakeProposed() with { DiscSlug = string.Empty });

        var result = await change.ValidateAsync(db, originalSnapshotJson: null, CancellationToken.None);

        await Assert.That(result.IsConflict).IsTrue();
    }

    [Test]
    public async Task ValidateAsync_ReturnsConflict_WhenBothParentSlugsSupplied()
    {
        using var db = ChangeTestSeed.CreateDbContext();
        ChangeTestSeed.Seed(db);

        var change = new TrackFieldsUpdate(MakeProposed() with { BoxsetSlug = "some-boxset" });

        var result = await change.ValidateAsync(db, originalSnapshotJson: null, CancellationToken.None);

        await Assert.That(result.IsConflict).IsTrue();
    }
}
