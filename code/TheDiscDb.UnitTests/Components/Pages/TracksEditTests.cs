using TheDiscDb.Components.Pages;
using TheDiscDb.Data.Changes.Track;
using TheDiscDb.InputModels;

namespace TheDiscDb.UnitTests.Components.Pages;

public class TracksEditTests
{
    [Test]
    [Arguments("Audio", null, "Director commentary")]
    [Arguments("Subtitles", "Forced subtitles", "Foreign dialogue only")]
    [Arguments("Subtitle", "Forced subtitles", "")]
    [Arguments("Audio", "Director commentary", null)]
    public async Task Row_ProposesOnlyDescriptionChanges(string type, string? originalName, string? proposedName)
    {
        var track = new Track
        {
            Index = 8,
            Type = type,
            Name = "Original codec",
            AudioType = "5.1",
            LanguageCode = "eng",
            Language = "English",
            Description = originalName,
        };
        var original = TrackFieldsUpdate.SnapshotFrom(track, "movie", null, "release", "disc", 2);
        var row = new TracksEdit.TrackEditRow(track, original) { EditDescription = proposedName };

        await Assert.That(row.HasChange).IsTrue();
        await Assert.That(row.BuildProposed()).IsEqualTo(original with { Description = proposedName ?? string.Empty });
        await Assert.That(track.Description).IsEqualTo(originalName);
    }

    [Test]
    [Arguments(null, null)]
    [Arguments(null, "")]
    [Arguments("", null)]
    [Arguments("Commentary", "Commentary")]
    public async Task Row_UnchangedNamesDoNotProduceChanges(string? originalName, string? proposedName)
    {
        var track = new Track { Type = "Audio", Description = originalName };
        var original = TrackFieldsUpdate.SnapshotFrom(track, "movie", null, "release", "disc", 2);
        var row = new TracksEdit.TrackEditRow(track, original);

        await Assert.That(row.HasChange).IsFalse();
        row.EditDescription = proposedName;
        await Assert.That(row.HasChange).IsFalse();
    }
}