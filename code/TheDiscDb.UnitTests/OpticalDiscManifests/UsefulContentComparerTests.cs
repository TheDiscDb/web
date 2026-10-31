namespace TheDiscDb.UnitTests.OpticalDiscManifests;

using System.Globalization;
using MakeMkv;
using TheDiscDb.Contributions.OpticalDiscManifest;

public class UsefulContentComparerTests
{
    [Test]
    public async Task AliasedUsefulChoice_IsAvailableWithoutChangingLegacyScores()
    {
        var log = Disc("00010.mpls", "1,2", 60, 100);
        var scan = Disc("00001.m2ts", "1,2", 60, 100);
        var manifest = Manifest("blu-ray", (scan.Titles[0], new[] { (1, 30d), (2, 30d) }));
        var before = IdentifyExperienceComparer.Compare(log, scan);

        var result = UsefulContentComparer.Compare(log, manifest, scan, [Reference(log)]);

        await Assert.That(result.Available).IsEqualTo(1);
        await Assert.That(result.AllKnownUsefulAvailable).IsTrue();
        await Assert.That(IdentifyExperienceComparer.Compare(log, scan).Similarity!.Overall)
            .IsEqualTo(before.Similarity!.Overall);
        await Assert.That(scan.Titles[0].Playlist).IsEqualTo("00001.m2ts");
    }

    [Test]
    [Arguments(1.001, true)]
    [Arguments(10.0, false)]
    public async Task CompleteCoreWithAddedFragments_DistinguishesTinyBoundariesFromIndependentChoiceReview(double tail, bool available)
    {
        var log = Disc("00001.mpls(1)", "1", 60, 100);
        var container = Disc("00001.mpls(1)", "1,2", 180, 500);
        var alternative = Disc("00002.mpls", "1,3", 60 + tail, 120);
        var scan = new DiscInfo { Titles = [container.Titles[0], alternative.Titles[0]] };
        var manifest = Manifest("blu-ray",
            (container.Titles[0], new[] { (1, 60d), (2, 120d) }),
            (alternative.Titles[0], new[] { (1, 60d), (3, tail) }));
        manifest.Disc!.Files.Add(new() { Path = "BDMV/STREAM/00001.m2ts", SizeBytes = 100 });

        var result = UsefulContentComparer.Compare(log, manifest, scan, [Reference(log)]);

        await Assert.That(result.Choices[0].IsAvailable).IsEqualTo(available);
        await Assert.That(result.Choices[0].Availability).IsEqualTo(available
            ? UsefulChoiceAvailability.AvailableWithSmallFragments : UsefulChoiceAvailability.BoundaryReview);
        await Assert.That(result.RequiredUnresolved).IsEqualTo(available ? 0 : 1);
        if (available)
        {
            await Assert.That(result.Choices[0].ScanRow).IsEqualTo(2);
        }
    }

    [Test]
    public async Task DVD_DoesNotGuessAcrossTitleNumbers_AndNormalizesPadding()
    {
        var log = Disc("01", "1-5", 60, 100);
        var scan = Disc("2", "1-5", 60, 100);
        var manifest = Manifest("dvd", (scan.Titles[0], Array.Empty<(int, double)>()));
        var different = UsefulContentComparer.Compare(log, manifest, scan, [Reference(log)]);
        scan.Titles[0].Playlist = "1";
        var same = UsefulContentComparer.Compare(log, manifest, scan, [Reference(log)]);

        await Assert.That(different.Available).IsEqualTo(0);
        await Assert.That(same.Available).IsEqualTo(1);
    }

    [Test]
    public async Task ExplicitAngles_AreNotFlattenedIntoAPass()
    {
        var log = Disc("00001.mpls", "1", 60, 100);
        var scan = Disc("00001.mpls", "1,2", 60, 100);
        var manifest = Manifest("blu-ray", (scan.Titles[0], new[] { (1, 60d), (2, 60d) }));
        manifest.Disc!.Titles[0].Segments[0].Angle = 1;
        manifest.Disc.Titles[0].Segments[1].Angle = 2;

        var result = UsefulContentComparer.Compare(log, manifest, scan, [Reference(log)]);

        await Assert.That(result.Available).IsEqualTo(0);
        await Assert.That(result.Choices[0].Availability).IsEqualTo(UsefulChoiceAvailability.AngleReview);
    }

    [Test]
    public async Task UnknownUsefulnessAndInvalidAcquisitions_AreNotPasses()
    {
        var log = Disc("1", "1", 2, 100);
        var manifest = Manifest("dvd", (log.Titles[0], Array.Empty<(int, double)>()));
        var unknown = UsefulContentComparer.Compare(log, manifest, log, []);
        var invalid = UsefulContentComparer.Compare(log, manifest, log, [Reference(log)], differentDisc: true);
        var inconsistent = UsefulContentComparer.Compare(log, manifest, log, [Reference(log)], acquisitionReview: true);

        await Assert.That(unknown.Status).IsEqualTo(UsefulContentStatus.NoUsefulReferences);
        await Assert.That(unknown.UnclassifiedReferenceRows).IsEqualTo(1);
        await Assert.That(unknown.AllKnownUsefulAvailable).IsFalse();
        await Assert.That(invalid.Status).IsEqualTo(UsefulContentStatus.NotComparable);
        await Assert.That(invalid.Available).IsEqualTo(0);
        await Assert.That(inconsistent.Status).IsEqualTo(UsefulContentStatus.AcquisitionReview);
        await Assert.That(inconsistent.Available).IsEqualTo(0);
    }

    [Test]
    public async Task ShortNamedExtra_IsCounted_WhileExplicitIncidentalIsSeparate()
    {
        var log = Disc("1", "1", 2, 100);
        var manifest = Manifest("dvd", (log.Titles[0], Array.Empty<(int, double)>()));
        var useful = UsefulContentComparer.Compare(log, manifest, log, [Reference(log) with { Required = false }]);
        var incidental = UsefulContentComparer.Compare(log, manifest, log, [Reference(log) with { Incidental = true }]);

        await Assert.That(useful.KnownUseful).IsEqualTo(1);
        await Assert.That(useful.Available).IsEqualTo(1);
        await Assert.That(incidental.KnownUseful).IsEqualTo(0);
        await Assert.That(incidental.IncidentalReferences).IsEqualTo(1);
        await Assert.That(incidental.AllKnownUsefulAvailable).IsFalse();
    }

    [Test]
    public async Task DuplicateReferenceOccurrences_AreNotSilentlyCollapsed()
    {
        var log = Disc("1", "1", 60, 100);
        log.Titles.Add(new Title { Playlist = "1", SegmentMap = "1", Length = "0:01:00", Size = 100 });
        var scan = Disc("1", "1", 60, 100);
        var manifest = Manifest("dvd", (scan.Titles[0], Array.Empty<(int, double)>()));

        var result = UsefulContentComparer.Compare(log, manifest, scan, [Reference(log), Reference(log)]);

        await Assert.That(result.KnownUseful).IsEqualTo(2);
        await Assert.That(result.AllKnownUsefulAvailable).IsFalse();
        await Assert.That(result.Choices.All(choice => choice.Availability == UsefulChoiceAvailability.ReferenceReview)).IsTrue();
    }

    [Test]
    public async Task MissingDurationAndMaterialTimingDifferences_AreNotTolerated()
    {
        var log = Disc("1", "1", 60, 100);
        var scan = Disc("1", "1", 50, 100);
        var manifest = Manifest("dvd", (scan.Titles[0], Array.Empty<(int, double)>()));
        var timing = UsefulContentComparer.Compare(log, manifest, scan, [Reference(log)]);
        scan.Titles[0].Length = null;
        var absent = UsefulContentComparer.Compare(log, manifest, scan, [Reference(log)]);

        await Assert.That(timing.Choices[0].Availability).IsEqualTo(UsefulChoiceAvailability.TimingReview);
        await Assert.That(absent.Available).IsEqualTo(0);
    }

    [Test]
    [Arguments("1,2,3", true)]
    [Arguments("1,20,3", false)]
    public async Task TruncatedReferenceMap_RequiresAnExactClipBoundaryPrefix(string actualMap, bool available)
    {
        var log = Disc("00001.mpls", "1,2,...", 60, 100);
        var scan = Disc("00001.mpls", actualMap, 60, 100);
        var manifest = Manifest("blu-ray", (scan.Titles[0], Array.Empty<(int, double)>()));

        var result = UsefulContentComparer.Compare(log, manifest, scan, [Reference(log)]);

        await Assert.That(result.Choices[0].IsAvailable).IsEqualTo(available);
    }

    [Test]
    public async Task SmallFragmentEquivalence_RequiresMatchingSharedFileBytes()
    {
        var log = Disc("00001.mpls", "1", 60, 100);
        var scan = Disc("00001.mpls", "1,2", 61, 110);
        var manifest = Manifest("blu-ray", (scan.Titles[0], new[] { (1, 60d), (2, 1d) }));
        manifest.Disc!.Files.Add(new() { Path = "BDMV/STREAM/00001.m2ts", SizeBytes = 99 });

        var result = UsefulContentComparer.Compare(log, manifest, scan, [Reference(log)]);

        await Assert.That(result.Available).IsEqualTo(0);
    }

    [Test]
    public async Task SemanticReview_NormalizesLanguagesAndDoesNotRebindNamedOrdinals()
    {
        var log = Disc("1", "1", 60, 100);
        var scan = Disc("1", "1", 60, 100);
        log.Titles[0].Segments = [
            new() { Type = "Audio", Name = "DTS-HD", LanguageCode = "eng" },
            new() { Type = "Audio", Name = "DTS", LanguageCode = "eng" },
            new() { Type = "Subtitles", Name = "Closed Captions", LanguageCode = "fre" }];
        scan.Titles[0].Segments = [
            new() { Type = "Audio", Name = "DTS-HD", LanguageCode = "en" },
            new() { Type = "Subtitles", Name = "PGS", LanguageCode = "fra" }];
        var manifest = Manifest("dvd", (scan.Titles[0], Array.Empty<(int, double)>()));
        var reference = Reference(log) with { Labels = [new UsefulStreamLabel("Audio", 2, "Commentary")] };

        var result = UsefulContentComparer.Compare(log, manifest, scan, [reference]);
        var streams = result.Choices[0].Streams!;

        await Assert.That(result.Available).IsEqualTo(1);
        await Assert.That(streams.AudioLanguageSignals.Count).IsEqualTo(0);
        await Assert.That(streams.SubtitleLanguageSignals.Count).IsEqualTo(0);
        await Assert.That(streams.NamedOrdinalsOutsideScan).IsEqualTo(1);
        await Assert.That(streams.NamedOrdinalMappingNeedsReview).IsTrue();
        await Assert.That(streams.NamedLabelsNotSemanticallyVerified).IsEqualTo(1);
        await Assert.That(streams.ReferenceClosedCaptions).IsTrue();
        await Assert.That(streams.DeclaredClosedCaptions).IsFalse();
        await Assert.That(reference.Labels![0].Ordinal).IsEqualTo(2);
        await Assert.That(scan.Titles[0].Segments.Count).IsEqualTo(2);
    }

    [Test]
    public async Task Aggregate_SeparatesMissingMeasurementsUnknownLabelsAndAcquisitionReview()
    {
        var log = Disc("1", "1", 60, 100);
        var manifest = Manifest("dvd", (log.Titles[0], Array.Empty<(int, double)>()));
        var measured = UsefulContentComparer.Compare(log, manifest, log, [Reference(log)]);
        var unknown = UsefulContentComparer.Compare(log, manifest, log, []);
        var acquisition = UsefulContentComparer.Compare(log, manifest, log, [Reference(log)], acquisitionReview: true);
        var future = new UsefulContentComparison { Version = 99 };

        var aggregate = UsefulContentAggregate.Create([measured, unknown, acquisition, future, null]);

        await Assert.That(aggregate.MeasuredDiscs).IsEqualTo(1);
        await Assert.That(aggregate.CompleteKnownUsefulDiscs).IsEqualTo(1);
        await Assert.That(aggregate.NoUsefulReferences).IsEqualTo(1);
        await Assert.That(aggregate.AcquisitionReview).IsEqualTo(1);
        await Assert.That(aggregate.NotMeasured).IsEqualTo(2);
        await Assert.That(aggregate.KnownUseful).IsEqualTo(2);
        await Assert.That(aggregate.Available).IsEqualTo(1);
        await Assert.That(aggregate.RequiredUnresolved).IsEqualTo(1);
    }

    private static UsefulContentReference Reference(DiscInfo disc)
        => new("Useful choice", disc.Titles[0].Playlist!, disc.Titles[0].SegmentMap, disc.Titles[0].Length, Required: true);

    private static DiscInfo Disc(string source, string map, double seconds, long size)
        => new() { Titles = [new Title
        {
            Playlist = source, SegmentMap = map, Size = size,
            Length = TimeSpan.FromSeconds(Math.Floor(seconds)).ToString(@"h\:mm\:ss", CultureInfo.InvariantCulture)
        }] };

    private static OpticalDiscManifestDocument Manifest(string format, params (Title Title, (int Clip, double Seconds)[] Segments)[] titles)
        => new() { Disc = new OpticalDiscManifestDisc
        {
            Format = format,
            Titles = titles.Select(item => new OpticalDiscManifestTitle
            {
                Segments = item.Segments.Select(segment => new OpticalDiscManifestSegment
                {
                    Clip = $"{segment.Clip:00000}", DurationSeconds = segment.Seconds
                }).ToList()
            }).ToList()
        } };
}
