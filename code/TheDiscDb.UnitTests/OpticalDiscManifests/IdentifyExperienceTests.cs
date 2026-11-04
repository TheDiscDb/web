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
    public async Task ToDiscInfo_AddsMakeMkvDerivedTracksAfterNavigationCodedStreams()
    {
        var manifest = Manifest();
        manifest.Disc!.Titles[0].Streams =
        [
            new OpticalDiscManifestStream { Type = "audio", Category = "audio", Codec = "TrueHD", CodingTypeCode = 0x83, Language = "eng", AudioLayout = "stereo" },
            new OpticalDiscManifestStream { Type = "audio", Category = "audio", Codec = "DTS-HD Master Audio", CodingTypeCode = 0x86, Language = "fra" },
            new OpticalDiscManifestStream { Type = "audio", Category = "secondaryAudio", Codec = "AC-3 Plus", CodingTypeCode = 0x84 },
            new OpticalDiscManifestStream { Type = "audio", Codec = "TrueHD Atmos" },
            new OpticalDiscManifestStream { Type = "subtitle", Category = "presentationGraphics", Codec = "Presentation Graphics", CodingTypeCode = 0x90, Language = "spa" }
        ];

        var segments = OpticalDiscManifestMapper.ToDiscInfo(manifest).Titles[0].Segments;

        await Assert.That(string.Join("|", segments.Select(s => $"{s.Index}:{s.Type}:{s.Name} {s.AudioType}:{s.LanguageCode}"))).IsEqualTo(string.Join("|", new[]
        {
            "0:Audio:TrueHD Stereo:eng",
            "1:Audio:Dolby Digital Stereo:eng",
            "2:Audio:DTS-HD Master Audio :fra",
            "3:Audio:DTS :fra",
            "4:Audio:Dolby Digital plus :",
            "5:Audio:TrueHD Atmos :",
            "6:Subtitles:HDMV PGS Subtitles :spa",
            "7:Subtitles:HDMV PGS Subtitles :spa"
        }));
    }

    [Test]
    public async Task ToDiscInfo_InfersOnlyPredictableMultichannelLayouts()
    {
        var manifest = Manifest();
        manifest.Disc!.Titles[0].Streams =
        [
            new OpticalDiscManifestStream { Type = "audio", Category = "audio", Codec = "AC-3 Plus", CodingTypeCode = 0x84, FormatCode = 6 },
            new OpticalDiscManifestStream { Type = "audio", Category = "audio", Codec = "TrueHD", CodingTypeCode = 0x83, FormatCode = 12 },
            new OpticalDiscManifestStream { Type = "audio", Category = "audio", Codec = "DTS", CodingTypeCode = 0x82, FormatCode = 6 },
            new OpticalDiscManifestStream { Type = "audio", Category = "audio", Codec = "AC-3", CodingTypeCode = 0x81, FormatCode = 3, AudioLayout = "stereo" },
            new OpticalDiscManifestStream { Type = "audio", Category = "secondaryAudio", Codec = "AC-3 Plus Secondary Audio", CodingTypeCode = 0xA1, FormatCode = 6 },
            new OpticalDiscManifestStream { Type = "audio", Category = "audio", Codec = "DTS-HD Master Audio", CodingTypeCode = 0x86, FormatCode = 6, AudioLayout = "7.1" }
        ];

        var segments = OpticalDiscManifestMapper.ToDiscInfo(manifest).Titles[0].Segments;

        await Assert.That(string.Join("|", segments.Select(s => $"{s.Name}:{s.AudioType}"))).IsEqualTo(string.Join("|", new[]
        {
            "Dolby Digital plus:Surround 7.1",
            "Dolby Digital:Surround 5.1",
            "TrueHD:",
            "Dolby Digital:Surround 5.1",
            "DTS:Surround 5.1",
            "Dolby Digital:Stereo",
            "AC-3 Plus Secondary Audio:Surround 5.1",
            "DTS-HD Master Audio:Surround 7.1",
            "DTS:Surround 7.1"
        }));
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
        await Assert.That(log.Titles[0].Playlist).IsEqualTo("01");
        log.Titles[0].Playlist = "1";
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
        await Assert.That(result.Differences.IdentifyExperience.Similarity).IsNull();
    }

    [Test]
    public async Task Compare_NonzeroMinimumLengthExcludesReferenceFromAllMetrics()
    {
        var manifest = Manifest();
        var log = OpticalDiscManifestMapper.ToDiscInfo(manifest);
        const string logText = "MSG:3025,0,2,\"Durée, filtrée\",\"Modèle, localisé\",\"0:00:30\",\"1\"";

        var result = DiscLogManifestComparer.Compare(log, logText, manifest,
            OpticalDiscManifestMapper.ToDiscInfo(manifest), "HASH");

        await Assert.That(result.Status).IsEqualTo(DiscLogManifestComparisonStatus.Excluded);
        await Assert.That(result.MatchedTitleCount).IsEqualTo(0);
        await Assert.That(result.OrderMatches).IsFalse();
        await Assert.That(result.Differences.MinimumLength!.Status).IsEqualTo(MakeMkvMinimumLengthStatus.NonZero);
        await Assert.That(result.Differences.IdentifyExperience!.Status).IsEqualTo(IdentifyExperienceStatus.Excluded);
        await Assert.That(result.Differences.IdentifyExperience.Similarity).IsNull();
        await Assert.That(result.Differences.UsefulContent!.Status).IsEqualTo(UsefulContentStatus.Excluded);
        await Assert.That(result.Differences.UsefulContent.KnownUseful).IsEqualTo(0);
    }

    [Test]
    public async Task Compare_DifferentDiscStillTakesPrecedenceOverMinimumLengthExclusion()
    {
        var manifest = Manifest();
        var log = OpticalDiscManifestMapper.ToDiscInfo(manifest);

        var result = DiscLogManifestComparer.Compare(log,
            "MSG:3025,0,2,\"title\",\"template\",\"0:00:30\",\"1\"",
            manifest, OpticalDiscManifestMapper.ToDiscInfo(manifest), "OTHER");

        await Assert.That(result.Status).IsEqualTo(DiscLogManifestComparisonStatus.DifferentDisc);
        await Assert.That(result.Differences.MinimumLength!.Status).IsEqualTo(MakeMkvMinimumLengthStatus.NonZero);
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
        await Assert.That(result.Differences.IdentifyExperience.Similarity).IsNull();
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
        await Assert.That(restored.IdentifyExperience!.Version).IsEqualTo(IdentifyExperienceComparison.CurrentVersion);
        await Assert.That(restored.IdentifyExperience.Status).IsEqualTo(IdentifyExperienceStatus.Match);
    }

    [Test]
    public async Task JavaPath_ComparesLogAsIfJavaRuntimeNeverRan()
    {
        const string jvmLog = "MSG:1005,0,1,\"MakeMKV v1.17.7\",\"\"\nMSG:3344,0,0,\"Using Java runtime\",\"\"\n";
        var log = JavaDisc(
            ("00156.mpls", "174", 100, "menu"),   // duplicates the clip row below
            ("00010.mpls", "20", 200, null),      // the scan keeps this playlist
            ("00174.m2ts", "174", 100, null),
            ("00200.mpls", "175", 300, null),     // Java-only playlist name for clip 175
            ("00201.mpls", "175", 300, null),     // second Java-only row for the same clip
            ("00300.mpls", "176", 999, null));    // not the whole clip: size differs from the scan
        var scan = JavaDisc(
            ("00010.mpls", "20", 200, null),
            ("00174.m2ts", "174", 100, null),
            ("00175.m2ts", "175", 300, null),
            ("00176.m2ts", "176", 400, null));

        var (normalized, evidence) = MakeMkvJavaPathNormalizer.Normalize(jvmLog, log, scan);

        await Assert.That(evidence.Status).IsEqualTo(MakeMkvJavaPathStatus.Used);
        await Assert.That(string.Join(",", normalized.Titles.Select(t => t.Playlist)))
            .IsEqualTo("00010.mpls,00174.m2ts,00175.m2ts,00300.mpls");
        await Assert.That(evidence.RenamedRows).IsEquivalentTo(new[] { "Row 4: 00200.mpls -> 00175.m2ts" });
        await Assert.That(evidence.RemovedRows)
            .IsEquivalentTo(new[] { "Row 1: 00156.mpls duplicates 00174.m2ts", "Row 5: 00201.mpls duplicates 00175.m2ts" });
        await Assert.That(normalized.Titles.All(t => t.JavaComment is null)).IsTrue();
        await Assert.That(normalized.Titles[2].ChapterCount).IsEqualTo(0);
        await Assert.That(normalized.Titles[0].ChapterCount).IsEqualTo(1);
        await Assert.That(log.Titles[0].Playlist).IsEqualTo("00156.mpls");
        await Assert.That(log.Titles[0].JavaComment).IsEqualTo("menu");
    }

    [Test]
    [Arguments("MSG:3344,0,0,\"Using Java runtime\",\"\"\nMSG:4041,0,0,\"Failed to launch Java\",\"\"\n", MakeMkvJavaPathStatus.Failed)]
    [Arguments("MSG:1005,0,1,\"MakeMKV v1.17.7\",\"\"\n", MakeMkvJavaPathStatus.NotUsed)]
    [Arguments("TINFO:0,16,0,\"00200.mpls\"\n", MakeMkvJavaPathStatus.Unknown)]
    public async Task JavaPath_OtherLogsAreComparedUnchanged(string logText, MakeMkvJavaPathStatus status)
    {
        var log = JavaDisc(("00200.mpls", "175", 300, "comment"));
        var (normalized, evidence) = MakeMkvJavaPathNormalizer.Normalize(
            logText, log, JavaDisc(("00175.m2ts", "175", 300, null)));

        await Assert.That(evidence.Status).IsEqualTo(status);
        await Assert.That(evidence.Normalized).IsFalse();
        await Assert.That(normalized).IsSameReferenceAs(log);
    }

    private static DiscInfo JavaDisc(params (string Source, string Clip, long Size, string? Comment)[] rows) => new()
    {
        Titles = rows.Select((row, index) => new Title
        {
            Index = index,
            Playlist = row.Source,
            SegmentMap = row.Clip,
            Size = row.Size,
            ChapterCount = 1,
            Length = "0:00:10",
            JavaComment = row.Comment
        }).ToList()
    };

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
