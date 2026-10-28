namespace TheDiscDb.UnitTests.DataImport;

using TheDiscDb.Data.Import.Pipeline;
using TheDiscDb.InputModels;

public class TrackItemHandlerTests
{
    [Test]
    [Arguments(null, "Commentary")]
    [Arguments("Commentary", "Director commentary")]
    [Arguments("Commentary", null)]
    [Arguments("Commentary", "")]
    public async Task TryUpdate_CopiesDescriptionWithoutChangingStreamIdentity(string? original, string? proposed)
    {
        var handler = new TrackItemHandler();
        var existing = CreateTrack(original);
        var incoming = CreateTrack(proposed);

        await Assert.That(handler.IsMatch(existing, incoming)).IsTrue();
        handler.TryUpdate(existing, incoming);

        await Assert.That(existing.Description).IsEqualTo(proposed);
        await Assert.That(existing.Index).IsEqualTo(4);
        await Assert.That(existing.Name).IsEqualTo("DTS");
        await Assert.That(existing.AudioType).IsEqualTo("5.1");
        await Assert.That(existing.LanguageCode).IsEqualTo("eng");
    }

    [Test]
    [Arguments(null, "Forced subtitles")]
    [Arguments("Forced subtitles", "Foreign dialogue only")]
    [Arguments("Forced subtitles", null)]
    [Arguments("Forced subtitles", "")]
    public async Task TitleUpdate_UpdatesExistingTrackInPlace(string? original, string? proposed)
    {
        var existingTrack = CreateTrack(original);
        existingTrack.Type = "Subtitles";
        var incomingTrack = CreateTrack(proposed);
        incomingTrack.Type = "Subtitles";
        var existing = new Title();
        existing.Tracks.Add(existingTrack);
        var incoming = new Title();
        incoming.Tracks.Add(incomingTrack);

        var handler = new TitleItemHandler(new TrackItemHandler(), new DiscItemReferenceItemHandler());
        handler.TryUpdate(existing, incoming);

        await Assert.That(existing.Tracks.Count).IsEqualTo(1);
        await Assert.That(ReferenceEquals(existing.Tracks.Single(), existingTrack)).IsTrue();
        await Assert.That(existingTrack.Description).IsEqualTo(proposed);
    }

    private static Track CreateTrack(string? description) => new()
    {
        Index = 4,
        Name = "DTS",
        Type = "Audio",
        AudioType = "5.1",
        LanguageCode = "eng",
        Language = "English",
        Description = description,
    };
}