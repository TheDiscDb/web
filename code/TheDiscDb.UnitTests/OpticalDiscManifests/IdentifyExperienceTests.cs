using System.Text.Json;
using MakeMkv;
using TheDiscDb.Contributions.OpticalDiscManifest;

namespace TheDiscDb.UnitTests.OpticalDiscManifests;

public class IdentifyExperienceTests
{
    [Test]
    public async Task Compare_NavigationNamesAndExplicitStereoMatchLogDialogNames()
    {
        var manifest = Manifest();
        manifest.Disc!.Titles[0].Streams =
        [
            new OpticalDiscManifestStream { Type = "audio", Codec = "AC-3", AudioLayout = "stereo" },
            new OpticalDiscManifestStream { Type = "subtitle", Codec = "Presentation Graphics" }
        ];
        var log = OpticalDiscManifestMapper.ToDiscInfo(manifest);
        log.Titles[0].Segments.Clear();
        log.Titles[0].Segments.Add(new Segment { Type = "Audio", Name = "Dolby Digital", AudioType = "Stereo" });
        log.Titles[0].Segments.Add(new Segment { Type = "Subtitles", Name = "HDMV PGS Subtitles" });

        await Assert.That(Compare(manifest, log).Differences.IdentifyExperience!.Status)
            .IsEqualTo(IdentifyExperienceStatus.Match);
    }

    [Test]
    public async Task Compare_UnknownMultichannelLayoutRemainsADialogDifference()
    {
        var manifest = Manifest();
        manifest.Disc!.Titles[0].Streams =
        [
            new OpticalDiscManifestStream { Type = "audio", Codec = "TrueHD" }
        ];
        var log = OpticalDiscManifestMapper.ToDiscInfo(manifest);
        log.Titles[0].Segments[0].Name = "TrueHD Atmos";
        log.Titles[0].Segments[0].AudioType = "Surround 7.1";

        var experience = Compare(manifest, log).Differences.IdentifyExperience!;
        await Assert.That(experience.Status).IsEqualTo(IdentifyExperienceStatus.Mismatch);
        await Assert.That(experience.Differences.Single().Field).IsEqualTo("AudioDialogNames");
    }

    [Test]
    public async Task Compare_DisplayTranslationDoesNotFabricateDuplicateSubtitleTracks()
    {
        var manifest = Manifest();
        manifest.Disc!.Titles[0].Streams =
        [
            new OpticalDiscManifestStream { Type = "subtitle", Codec = "Presentation Graphics" }
        ];
        var log = OpticalDiscManifestMapper.ToDiscInfo(manifest);
        log.Titles[0].Segments.Add(new Segment { Type = "Subtitles", Name = "HDMV PGS Subtitles" });

        var experience = Compare(manifest, log).Differences.IdentifyExperience!;
        await Assert.That(experience.Status).IsEqualTo(IdentifyExperienceStatus.Mismatch);
        await Assert.That(experience.Differences.Single().Field).IsEqualTo("SubtitlesDialogNames");
        await Assert.That(OpticalDiscManifestMapper.ToDiscInfo(manifest).Titles[0].Segments.Count).IsEqualTo(1);
    }

    [Test]
    public async Task Compare_ExactBytesDifferButDisplayedValuesMatch()
    {
        var manifest = Manifest();
        var log = OpticalDiscManifestMapper.ToDiscInfo(manifest);
        log.Titles[0].Size++;
        var result = Compare(manifest, log);

        await Assert.That(result.Status).IsEqualTo(DiscLogManifestComparisonStatus.Mismatch);
        await Assert.That(result.Differences.IdentifyExperience!.Status).IsEqualTo(IdentifyExperienceStatus.Match);
        await Assert.That(result.Differences.IdentifyExperience.MatchingRowCount).IsEqualTo(1);
    }

    [Test]
    [Arguments("DisplaySize")]
    [Arguments("Comment")]
    [Arguments("Audio")]
    [Arguments("Subtitles")]
    [Arguments("AudioDialogNames")]
    [Arguments("SubtitlesDialogNames")]
    public async Task Compare_TechnicalMatchCanHaveIdentifyDifferences(string field)
    {
        var manifest = Manifest();
        var log = OpticalDiscManifestMapper.ToDiscInfo(manifest);
        switch (field)
        {
            case "DisplaySize": log.Titles[0].DisplaySize = "different displayed size"; break;
            case "Comment": log.Titles[0].JavaComment = "Main feature"; break;
            case "Audio": log.Titles[0].Segments.Clear(); break;
            case "Subtitles": log.Titles[0].Segments.Add(new Segment { Type = "Subtitles", Name = "PGS" }); break;
            case "AudioDialogNames": log.Titles[0].Segments[0].Name = "DTS"; break;
            case "SubtitlesDialogNames":
                manifest.Disc!.Titles[0].Streams.Add(new OpticalDiscManifestStream { Type = "subtitle", Codec = "PGS" });
                log.Titles[0].Segments.Add(new Segment { Type = "Subtitles", Name = "SRT" });
                break;
        }

        var result = Compare(manifest, log);
        await Assert.That(result.Status).IsEqualTo(DiscLogManifestComparisonStatus.Match);
        await Assert.That(result.Differences.IdentifyExperience!.Status).IsEqualTo(IdentifyExperienceStatus.Mismatch);
        await Assert.That(result.Differences.IdentifyExperience.Differences.Count).IsGreaterThan(0);
    }

    [Test]
    public async Task Compare_UsesFullTooltipAndPreservesDuplicateRows()
    {
        var log = new DiscInfo();
        var scan = new DiscInfo();
        log.Titles.Add(new Title { Playlist = "00001.mpls", SegmentMap = "123456789012,1" });
        scan.Titles.Add(new Title { Playlist = "00001.mpls", SegmentMap = "123456789012,2" });
        var tooltip = IdentifyExperienceComparer.Compare(log, scan);
        await Assert.That(tooltip.Differences.Single().Field).IsEqualTo("SegmentMapTooltip");
        log.Titles.Add(log.Titles[0]);
        var duplicate = IdentifyExperienceComparer.Compare(log, scan);
        await Assert.That(duplicate.LogRowCount).IsEqualTo(2);
        await Assert.That(duplicate.ManifestRowCount).IsEqualTo(1);
        await Assert.That(duplicate.Status).IsEqualTo(IdentifyExperienceStatus.Mismatch);
    }

    [Test]
    public async Task Compare_SameContentWithDifferentLabelOrOrderDoesNotMatch()
    {
        var manifest = Manifest();
        var log = OpticalDiscManifestMapper.ToDiscInfo(manifest);
        var scan = OpticalDiscManifestMapper.ToDiscInfo(manifest);
        scan.Titles[0].Playlist = "00001.m2ts";
        await Assert.That(IdentifyExperienceComparer.Compare(log, scan).Status).IsEqualTo(IdentifyExperienceStatus.Mismatch);
        log.Titles.Add(new Title { Playlist = "00002.mpls" });
        scan.Titles.Clear();
        scan.Titles.Add(log.Titles[1]);
        scan.Titles.Add(log.Titles[0]);
        var reordered = IdentifyExperienceComparer.Compare(log, scan);
        await Assert.That(reordered.Status).IsEqualTo(IdentifyExperienceStatus.Mismatch);
        await Assert.That(reordered.MatchingRowCount).IsEqualTo(0);
    }

    [Test]
    public async Task Compare_DvdPaddingIsVisibleEvenWhenTechnicalKeysMatch()
    {
        var manifest = Manifest();
        manifest.Disc!.Format = "dvd";
        manifest.Disc.Titles[0].Source = new OpticalDiscManifestTitleSource { Title = 1 };
        var log = OpticalDiscManifestMapper.ToDiscInfo(manifest);
        log.Titles[0].Playlist = "01";
        var result = Compare(manifest, log);
        await Assert.That(result.Status).IsEqualTo(DiscLogManifestComparisonStatus.Match);
        await Assert.That(result.Differences.IdentifyExperience!.Status).IsEqualTo(IdentifyExperienceStatus.Mismatch);
    }

    [Test]
    public async Task Compare_EmptyRowsAndDifferentDiscsAreNotComparable()
    {
        await Assert.That(IdentifyExperienceComparer.Compare(new DiscInfo(), new DiscInfo()).Status)
            .IsEqualTo(IdentifyExperienceStatus.NotComparable);
        var manifest = Manifest();
        var result = DiscLogManifestComparer.Compare(OpticalDiscManifestMapper.ToDiscInfo(manifest), "", manifest,
            OpticalDiscManifestMapper.ToDiscInfo(manifest), "OTHER");
        await Assert.That(result.Differences.IdentifyExperience!.Status).IsEqualTo(IdentifyExperienceStatus.NotComparable);
    }

    [Test]
    public async Task Compare_MissingMetadataIsComparedAsActuallyDisplayed()
    {
        var manifest = Manifest();
        var log = OpticalDiscManifestMapper.ToDiscInfo(manifest);
        manifest.Disc!.Titles[0].ChapterCount = null;
        manifest.Disc.Titles[0].DurationSeconds = null;
        var experience = Compare(manifest, log).Differences.IdentifyExperience!;
        await Assert.That(experience.Status).IsEqualTo(IdentifyExperienceStatus.Mismatch);
        await Assert.That(experience.Differences.Any(d => d.Field == "LabelChapters")).IsTrue();
    }

    [Test]
    public async Task Compare_CommentVisibilityAndTrackOrderMatter()
    {
        var manifest = Manifest();
        var log = OpticalDiscManifestMapper.ToDiscInfo(manifest);
        var scan = OpticalDiscManifestMapper.ToDiscInfo(manifest);
        log.Titles[0].JavaComment = " ";
        var comment = IdentifyExperienceComparer.Compare(log, scan);
        await Assert.That(comment.Differences.Any(d => d.Field == "CommentColumnVisible")).IsTrue();
        log.Titles[0].JavaComment = "";
        scan.Titles[0].JavaComment = null;
        await Assert.That(IdentifyExperienceComparer.Compare(log, scan).Status).IsEqualTo(IdentifyExperienceStatus.Match);
        log.Titles[0].Segments.Add(new Segment { Type = "audio", Name = "DTS", AudioType = "5.1" });
        scan.Titles[0].Segments.Insert(0, new Segment { Type = "Audio", Name = "DTS", AudioType = "5.1" });
        await Assert.That(IdentifyExperienceComparer.Compare(log, scan).Differences.Single().Field).IsEqualTo("AudioDialogNames");
    }

    [Test]
    public async Task Compare_ScanErrorCannotClaimSameExperience()
    {
        var manifest = Manifest();
        manifest.Extensions = new()
        {
            ["thediscdb.optical-disc-manifest/scan-diagnostics"] = JsonSerializer.SerializeToElement(new[]
            {
                new OpticalDiscScanDiagnostic { Code = "ODM_TEST_ERROR", Severity = "error", Message = "Incomplete scan" }
            }, OpticalDiscManifestDocument.SerializerOptions)
        };
        var result = Compare(manifest, OpticalDiscManifestMapper.ToDiscInfo(manifest));
        await Assert.That(result.Differences.IdentifyExperience!.Status).IsEqualTo(IdentifyExperienceStatus.NotComparable);
    }

    [Test]
    public async Task Json_OlderComparisonsRemainUnmeasuredAndNewMetricRoundTrips()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        var old = JsonSerializer.Deserialize<DiscLogManifestDifferences>("{}", options)!;
        await Assert.That(old.IdentifyExperience).IsNull();
        var manifest = Manifest();
        var result = Compare(manifest, OpticalDiscManifestMapper.ToDiscInfo(manifest));
        var restored = JsonSerializer.Deserialize<DiscLogManifestDifferences>(result.DifferencesJson, options)!;
        await Assert.That(restored.IdentifyExperience!.Version).IsEqualTo(1);
        await Assert.That(restored.IdentifyExperience.Status).IsEqualTo(IdentifyExperienceStatus.Match);
    }

    private static DiscLogManifestComparisonResult Compare(OpticalDiscManifestDocument manifest, DiscInfo log)
        => DiscLogManifestComparer.Compare(log, "", manifest, OpticalDiscManifestMapper.ToDiscInfo(manifest), "HASH");

    private static OpticalDiscManifestDocument Manifest() => new()
    {
        SchemaVersion = 1,
        Disc = new OpticalDiscManifestDisc
        {
            Format = "blu-ray",
            Identifiers = [new OpticalDiscManifestIdentifier { Kind = OpticalDiscManifestIdentifier.ContentHashKind, Value = "HASH" }],
            Titles =
            [
                new OpticalDiscManifestTitle
                {
                    Source = new OpticalDiscManifestTitleSource { Path = "BDMV/PLAYLIST/00001.mpls" },
                    ChapterCount = 2,
                    SizeBytes = 1_000_000,
                    DisplaySize = "1 MB",
                    DurationSeconds = 10,
                    Segments = [new OpticalDiscManifestSegment { Clip = "1" }],
                    Streams = [new OpticalDiscManifestStream { Type = "audio", Codec = "AC3", AudioLayout = "5.1" }]
                }
            ]
        }
    };
}
