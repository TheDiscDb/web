namespace TheDiscDb.UnitTests.OpticalDiscManifests;

using MakeMkv;
using TheDiscDb.Contributions.OpticalDiscManifest;

public class OpticalDiscManifestValidatorTests
{
    private static readonly OpticalDiscManifestValidator Validator = new();

    private const string MinimalManifest = """
        {
          "schemaVersion": 1,
          "producer": { "name": "thediscdb", "version": "1.0.0" },
          "disc": {
            "format": "blu-ray",
            "identifiers": [
              { "kind": "thediscdb-content-hash", "value": "57B059114B517DF43BE4D05FCA0869FA" }
            ]
          }
        }
        """;

    [Test]
    public async Task Parse_MinimalValidManifest_Succeeds()
    {
        var result = Validator.Parse(MinimalManifest);

        await Assert.That(result.Error).IsNull();
        await Assert.That(result.IsValid).IsTrue();
        await Assert.That(result.Document!.Disc!.Format).IsEqualTo("blu-ray");
    }

    [Test]
    public async Task Parse_RealConverterOutput_Succeeds()
    {
        var result = Validator.Parse(await TestFiles.ReadManifestAsync());

        await Assert.That(result.Error).IsNull();
        await Assert.That(result.IsValid).IsTrue();
    }

    [Test]
    public async Task Parse_EmptyContent_ReportsEmptyFile()
    {
        var result = Validator.Parse("   ");

        await Assert.That(result.IsValid).IsFalse();
        await Assert.That(result.Error).Contains("empty");
    }

    [Test]
    public async Task Parse_Malformed_ReportsInvalidJson()
    {
        var result = Validator.Parse("{ not json");

        await Assert.That(result.IsValid).IsFalse();
        await Assert.That(result.Error).Contains("not valid JSON");
    }

    [Test]
    public async Task Parse_WrongSchemaVersion_IsRejected()
    {
        var result = Validator.Parse(MinimalManifest.Replace("\"schemaVersion\": 1", "\"schemaVersion\": 2"));

        await Assert.That(result.IsValid).IsFalse();
        await Assert.That(result.Error).IsNotNull();
    }

    [Test]
    public async Task Parse_MissingContentHash_IsRejected()
    {
        // TheDiscDb keys pressings off the content hash, so the schema requires one.
        const string NoHash = """
            {
              "schemaVersion": 1,
              "producer": { "name": "thediscdb", "version": "1.0.0" },
              "disc": { "format": "blu-ray", "identifiers": [] }
            }
            """;

        var result = Validator.Parse(NoHash);

        await Assert.That(result.IsValid).IsFalse();
        await Assert.That(result.Error).Contains("Optical Disc Manifest v1 schema");
    }

    [Test]
    public async Task Parse_UnknownDiscFormat_IsRejected()
    {
        var result = Validator.Parse(MinimalManifest.Replace("\"blu-ray\"", "\"laserdisc\""));

        await Assert.That(result.IsValid).IsFalse();
        await Assert.That(result.Error).IsNotNull();
    }

    [Test]
    public async Task Parse_MissingDisc_IsRejected()
    {
        const string NoDisc = """
            { "schemaVersion": 1, "producer": { "name": "thediscdb", "version": "1.0.0" } }
            """;

        var result = Validator.Parse(NoDisc);

        await Assert.That(result.IsValid).IsFalse();
    }
}

public class OpticalDiscManifestMapperTests
{
    private static async Task<DiscInfo> MapSampleAsync()
    {
        var result = new OpticalDiscManifestValidator().Parse(await TestFiles.ReadManifestAsync());
        await Assert.That(result.IsValid).IsTrue();
        return OpticalDiscManifestMapper.ToDiscInfo(result.Document!);
    }

    private static async Task<DiscInfo> ParseSampleLogAsync()
    {
        // LogParser.Parse(string) takes a path, so feed it the lines instead.
        string[] lines = await File.ReadAllLinesAsync(TestFiles.LogPath);
        return LogParser.Organize(LogParser.Parse(lines));
    }

    [Test]
    public async Task ToDiscInfo_ReproducesTheLogItWasConvertedFrom()
    {
        // The fixtures are the same disc: the .txt is a real MakeMKV log and the .odm.json is what
        // the convert-makemkv-logs tool produced from it. Every title that survived conversion has
        // to map back to exactly what the parser reports, or the identify flow would show different
        // data depending on which file a contributor uploaded.
        DiscInfo fromManifest = await MapSampleAsync();
        DiscInfo fromLog = await ParseSampleLogAsync();

        // Disc name is deliberately not compared. The converter prefers the name from the sibling
        // disc*.json (TheDiscDb's own label, such as "Blu-ray") over MakeMKV's volume label, so the
        // two disagree by design. Nothing in the identify flow reads DiscInfo.Name.
        await Assert.That(fromManifest.Name).IsNotNull();
        await Assert.That(fromManifest.Type).IsEqualTo(fromLog.Type);
        await Assert.That(fromManifest.HashInfo.Count).IsEqualTo(fromLog.HashInfo.Count);

        // Titles are matched on playlist rather than position, since the manifest carries no MakeMKV
        // title index. Every log title is representable in the manifest, so the counts agree.
        await Assert.That(fromManifest.Titles.Count).IsGreaterThan(0);
        await Assert.That(fromManifest.Titles.Count).IsEqualTo(fromLog.Titles.Count);

        var logTitlesByPlaylist = fromLog.Titles
            .Where(title => !string.IsNullOrEmpty(title.Playlist))
            .GroupBy(title => title.Playlist!)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);

        foreach (Title actual in fromManifest.Titles)
        {
            await Assert.That(actual.Playlist).IsNotNull();
            await Assert.That(logTitlesByPlaylist.ContainsKey(actual.Playlist!)).IsTrue();

            Title expected = logTitlesByPlaylist[actual.Playlist!];

            await Assert.That(actual.Length).IsEqualTo(expected.Length);
            await Assert.That(actual.Size).IsEqualTo(expected.Size);
            await Assert.That(actual.DisplaySize).IsEqualTo(expected.DisplaySize);
            await Assert.That(actual.ChapterCount).IsEqualTo(expected.ChapterCount);
            await Assert.That(actual.SegmentMap).IsEqualTo(expected.SegmentMap);
            await Assert.That(actual.Segments.Count).IsEqualTo(expected.Segments.Count);

            for (int s = 0; s < expected.Segments.Count; s++)
            {
                Segment expectedSegment = expected.Segments[s];
                Segment actualSegment = actual.Segments[s];

                await Assert.That(actualSegment.Type).IsEqualTo(expectedSegment.Type);
                await Assert.That(actualSegment.Name).IsEqualTo(expectedSegment.Name);
                await Assert.That(actualSegment.LanguageCode).IsEqualTo(expectedSegment.LanguageCode);
                await Assert.That(actualSegment.Language).IsEqualTo(expectedSegment.Language);
                await Assert.That(actualSegment.AudioType).IsEqualTo(expectedSegment.AudioType);
                await Assert.That(actualSegment.Resolution).IsEqualTo(expectedSegment.Resolution);
                await Assert.That(actualSegment.AspectRatio).IsEqualTo(expectedSegment.AspectRatio);
            }
        }

        for (int i = 0; i < fromLog.HashInfo.Count; i++)
        {
            await Assert.That(fromManifest.HashInfo[i].Name).IsEqualTo(fromLog.HashInfo[i].Name);
            await Assert.That(fromManifest.HashInfo[i].Size).IsEqualTo(fromLog.HashInfo[i].Size);
        }
    }

    [Test]
    public async Task ToDiscInfo_FormatsSegmentMapTheWayMakeMkvDoes()
    {
        // MakeMKV writes field 26 as clip numbers without leading zeros, e.g.
        // "250,1066,1067". The manifest names the same clips as "00250", so the mapper has to
        // normalize or manifest-sourced discs would not compare against log-sourced ones.
        const string WithSegments = """
            {
              "schemaVersion": 1,
              "producer": { "name": "thediscdb", "version": "1.0.0" },
              "disc": {
                "format": "blu-ray",
                "identifiers": [
                  { "kind": "thediscdb-content-hash", "value": "57B059114B517DF43BE4D05FCA0869FA" }
                ],
                "titles": [
                  {
                    "source": { "path": "BDMV/PLAYLIST/00249.mpls" },
                    "segments": [
                      { "clip": "00250" },
                      { "clip": "01066" },
                      { "clip": "BDMV/STREAM/01067.m2ts" }
                    ]
                  }
                ]
              }
            }
            """;

        var result = new OpticalDiscManifestValidator().Parse(WithSegments);
        await Assert.That(result.Error).IsNull();

        DiscInfo info = OpticalDiscManifestMapper.ToDiscInfo(result.Document!);

        await Assert.That(info.Titles[0].SegmentMap).IsEqualTo("250,1066,1067");
    }

    [Test]
    public async Task ToDiscInfo_WritesStereoscopicSegmentsAsBaseSlashDependent()
    {
        // MakeMKV writes a 3D segment as "base/dependent": Super Mario Bros (3D Blu-ray)
        // 01005.mpls reads "7/8", and multi-segment 3D titles read "30/31,32/33".
        const string With3D = """
            {
              "schemaVersion": 1,
              "producer": { "name": "thediscdb", "version": "1.0.0" },
              "disc": {
                "format": "blu-ray",
                "identifiers": [
                  { "kind": "thediscdb-content-hash", "value": "57B059114B517DF43BE4D05FCA0869FA" }
                ],
                "titles": [
                  {
                    "source": { "path": "BDMV/PLAYLIST/01005.mpls" },
                    "segments": [
                      { "clip": "00030", "dependentClip": "00031" },
                      { "clip": "00032", "dependentClip": "00033" },
                      { "clip": "00012" }
                    ]
                  }
                ]
              }
            }
            """;

        var result = new OpticalDiscManifestValidator().Parse(With3D);
        await Assert.That(result.Error).IsNull();

        DiscInfo info = OpticalDiscManifestMapper.ToDiscInfo(result.Document!);

        await Assert.That(info.Titles[0].SegmentMap).IsEqualTo("30/31,32/33,12");
    }

    [Test]
    public async Task ToDiscInfo_NamesSplitPlaylistPartsAndTruncatesLengthsLikeMakeMkv()
    {
        // MakeMKV splits a playlist at a non-seamless connection and names the later parts
        // "00005.mpls(1)". It also truncates lengths: a 92.9s title shows as 0:01:32.
        const string WithParts = """
            {
              "schemaVersion": 1,
              "producer": { "name": "thediscdb", "version": "1.0.0" },
              "disc": {
                "format": "blu-ray",
                "identifiers": [
                  { "kind": "thediscdb-content-hash", "value": "57B059114B517DF43BE4D05FCA0869FA" }
                ],
                "titles": [
                  { "source": { "path": "BDMV/PLAYLIST/00005.mpls", "part": 0 }, "durationSeconds": 2557.764 },
                  { "source": { "path": "BDMV/PLAYLIST/00005.mpls", "part": 1 }, "durationSeconds": 92.9 }
                ]
              }
            }
            """;

        var result = new OpticalDiscManifestValidator().Parse(WithParts);
        await Assert.That(result.Error).IsNull();

        DiscInfo info = OpticalDiscManifestMapper.ToDiscInfo(result.Document!);

        await Assert.That(info.Titles[0].Playlist).IsEqualTo("00005.mpls");
        await Assert.That(info.Titles[0].Length).IsEqualTo("0:42:37");
        await Assert.That(info.Titles[1].Playlist).IsEqualTo("00005.mpls(1)");
        await Assert.That(info.Titles[1].Length).IsEqualTo("0:01:32");
    }

    [Test]
    public async Task ToDiscInfo_AlwaysProducesASegmentMap()
    {
        // The identify flow sends SegmentMap to a non-null GraphQL input, so a null here fails the
        // mutation even for a title the producer described no segments for.
        DiscInfo info = await MapSampleAsync();

        foreach (Title title in info.Titles)
        {
            await Assert.That(title.SegmentMap).IsNotNull();
        }
    }

    [Test]
    public async Task ToDiscInfo_KeepsTheSegmentMapInPlaybackOrder()
    {
        // The segment map is ordered by playback, not by clip number, and it is what tells two
        // titles of the same length apart. Sorting it would quietly break that.
        DiscInfo info = await MapSampleAsync();

        Title? title = info.Titles.FirstOrDefault(t => t.Playlist == "00249.mpls");

        await Assert.That(title).IsNotNull();
        await Assert.That(title!.SegmentMap).IsEqualTo("250,1066,1067,1073,1068,1069,1070");
    }

    [Test]
    public async Task ToDiscInfo_StreamSourceBecomesTheClipFileName()
    {
        // Titles backed directly by a clip rather than a playlist are real extras on retail discs.
        // MakeMKV records the bare file name in the playlist field for these, exactly as it does
        // for a playlist-backed title, so the mapper must not treat the two differently.
        DiscInfo info = await MapSampleAsync();

        var streamTitles = info.Titles
            .Where(t => t.Playlist != null && t.Playlist.EndsWith(".m2ts", StringComparison.OrdinalIgnoreCase))
            .ToList();

        await Assert.That(streamTitles).IsNotEmpty();

        foreach (Title title in streamTitles)
        {
            await Assert.That(title.Playlist).DoesNotContain("/");
            await Assert.That(title.Playlist).DoesNotContain("BDMV");
        }
    }

    [Test]
    public async Task ToDiscInfo_KeepsEveryTitleTheLogDescribes()
    {
        // A title whose source cannot be expressed in the manifest has to be dropped outright,
        // because the manifest requires a source. That silently loses real content, so the sample
        // disc - which mixes playlist-backed and stream-backed titles - must survive intact.
        DiscInfo fromLog = await ParseSampleLogAsync();
        DiscInfo fromManifest = await MapSampleAsync();

        await Assert.That(fromManifest.Titles.Count).IsEqualTo(fromLog.Titles.Count);
    }

    [Test]
    public async Task ToDiscInfo_NumbersTitlesSequentially()
    {
        // The manifest carries no MakeMKV title index, so ordinals are the only stable numbering
        // available. They just have to be dense and in order for the identify flow to address them.
        DiscInfo info = await MapSampleAsync();

        for (int i = 0; i < info.Titles.Count; i++)
        {
            await Assert.That(info.Titles[i].Index).IsEqualTo(i);
        }
    }

    [Test]
    public async Task ToDiscInfo_UsesTheSegmentTypeNamesTheIdentifyFlowMatchesOn()
    {
        // IdentifyDiscItems compares against these exact strings, and MakeMKV pluralizes
        // "Subtitles" while the manifest uses the singular "subtitle".
        DiscInfo info = await MapSampleAsync();
        var types = info.Titles.SelectMany(t => t.Segments).Select(s => s.Type).Distinct().ToList();

        await Assert.That(types).Contains("Video");
        await Assert.That(types).Contains("Audio");
        await Assert.That(types).Contains("Subtitles");
        await Assert.That(types).DoesNotContain("subtitle");
    }

    [Test]
    public async Task ToDiscInfo_DvdSourceBecomesTheTitleNumber()
    {
        var document = new OpticalDiscManifestDocument
        {
            SchemaVersion = 1,
            Disc = new OpticalDiscManifestDisc
            {
                Format = "dvd",
                Titles =
                {
                    new OpticalDiscManifestTitle { Source = new OpticalDiscManifestTitleSource { Title = 7 } }
                }
            }
        };

        DiscInfo info = OpticalDiscManifestMapper.ToDiscInfo(document);

        await Assert.That(info.Type).IsEqualTo("DVD disc");
        await Assert.That(info.Titles[0].Playlist).IsEqualTo("7");
    }

    [Test]
    public async Task ToDiscInfo_DvdCellsBecomeMakeMkvSegmentMapAndWholeSecondLength()
    {
        // Alexander disc 1 title 1: VOB 6 cells 1-3 then VOB 7 cell 1, as "{vobId}.{cellId}".
        // MakeMKV numbers cells in play order, joins runs from the same VOB and sums whole
        // cell seconds (197 + 152 + 208 + 5 = 562 s), ignoring the frame-accurate total.
        var title = new OpticalDiscManifestTitle
        {
            Source = new OpticalDiscManifestTitleSource { Title = 1 },
            DurationSeconds = 564.997,
        };
        foreach (var (clip, seconds) in new[] { ("6.1", 197.834), ("6.2", 152.5), ("6.3", 208.701), ("7.1", 5.968) })
        {
            title.Segments.Add(new OpticalDiscManifestSegment { Clip = clip, DurationSeconds = seconds });
        }

        var document = new OpticalDiscManifestDocument
        {
            SchemaVersion = 1,
            Disc = new OpticalDiscManifestDisc { Format = "dvd", Titles = { title } }
        };

        DiscInfo info = OpticalDiscManifestMapper.ToDiscInfo(document);

        await Assert.That(info.Titles[0].SegmentMap).IsEqualTo("1-3,4");
        await Assert.That(info.Titles[0].Length).IsEqualTo("0:09:22");
    }

    [Test]
    public async Task ToDiscInfo_DvdSegmentMapKeepsPgcCellNumbersOfTrimmedTitles()
    {
        // United 93 title 3 plays cells 3 (VOB 3), 4 (VOB 4) and 5 (VOB 5) of its PGC; MakeMKV
        // skips cells 1-2 and keeps their numbering, reporting "3,4,5" and no chapters.
        var title = new OpticalDiscManifestTitle { Source = new OpticalDiscManifestTitleSource { Title = 3 } };
        foreach (var (clip, cell) in new[] { ("3.1", 3), ("4.1", 4), ("5.1", 5) })
        {
            title.Segments.Add(new OpticalDiscManifestSegment { Clip = clip, Cell = cell, DurationSeconds = 1 });
        }

        var document = new OpticalDiscManifestDocument
        {
            SchemaVersion = 1,
            Disc = new OpticalDiscManifestDisc { Format = "dvd", Titles = { title } }
        };

        DiscInfo info = OpticalDiscManifestMapper.ToDiscInfo(document);

        await Assert.That(info.Titles[0].SegmentMap).IsEqualTo("3,4,5");
        await Assert.That(info.Titles[0].ChapterCount).IsEqualTo(0);
    }

    [Test]
    public async Task ToDiscInfo_BluRaySourceBecomesThePlaylistFileName()
    {
        var document = new OpticalDiscManifestDocument
        {
            SchemaVersion = 1,
            Disc = new OpticalDiscManifestDisc
            {
                Format = "uhd-blu-ray",
                Titles =
                {
                    new OpticalDiscManifestTitle
                    {
                        Source = new OpticalDiscManifestTitleSource { Path = "BDMV/PLAYLIST/00800.mpls" }
                    }
                }
            }
        };

        DiscInfo info = OpticalDiscManifestMapper.ToDiscInfo(document);

        // MakeMKV reports UHD pressings as Blu-ray, and records only the playlist file name.
        await Assert.That(info.Type).IsEqualTo("Blu-ray disc");
        await Assert.That(info.Titles[0].Playlist).IsEqualTo("00800.mpls");
    }

    [Test]
    [Arguments(6214, "1:43:34")]
    [Arguments(0, "0:00:00")]
    [Arguments(59, "0:00:59")]
    [Arguments(36000, "10:00:00")]
    public async Task ToDiscInfo_FormatsDurationsTheWayMakeMkvDoes(double seconds, string expected)
    {
        var document = new OpticalDiscManifestDocument
        {
            SchemaVersion = 1,
            Disc = new OpticalDiscManifestDisc
            {
                Format = "blu-ray",
                Titles = { new OpticalDiscManifestTitle { DurationSeconds = seconds } }
            }
        };

        DiscInfo info = OpticalDiscManifestMapper.ToDiscInfo(document);

        await Assert.That(info.Titles[0].Length).IsEqualTo(expected);
        await Assert.That(info.Titles[0].LengthAsTimeSpan).IsEqualTo(TimeSpan.FromSeconds(seconds));
    }

    [Test]
    [Arguments(82825216L, "78.9 MB")]
    [Arguments(13604864L, "12.9 MB")]
    [Arguments(442368L, "0.4 MB")]
    [Arguments(2164613120L, "2.0 GB")]
    [Arguments(5815142400L, "5.4 GB")]
    public async Task ToDiscInfo_TruncatesDisplaySizeLikeMakeMkv(long sizeBytes, string expected)
    {
        var document = new OpticalDiscManifestDocument
        {
            SchemaVersion = 1,
            Disc = new OpticalDiscManifestDisc
            {
                Format = "dvd",
                Titles = { new OpticalDiscManifestTitle { SizeBytes = sizeBytes } }
            }
        };

        DiscInfo info = OpticalDiscManifestMapper.ToDiscInfo(document);

        await Assert.That(info.Titles[0].DisplaySize).IsEqualTo(expected);
    }

    [Test]
    public async Task ToDiscInfo_FallsBackToTheChapterListWhenChapterCountIsAbsent()
    {
        var document = new OpticalDiscManifestDocument
        {
            SchemaVersion = 1,
            Disc = new OpticalDiscManifestDisc
            {
                Format = "blu-ray",
                Titles =
                {
                    new OpticalDiscManifestTitle
                    {
                        Chapters =
                        {
                            new OpticalDiscManifestChapter { StartSeconds = 0 },
                            new OpticalDiscManifestChapter { StartSeconds = 60 }
                        }
                    }
                }
            }
        };

        DiscInfo info = OpticalDiscManifestMapper.ToDiscInfo(document);

        await Assert.That(info.Titles[0].ChapterCount).IsEqualTo(2);
    }

    [Test]
    public async Task GetContentHash_ReturnsTheHashTheDatabaseKeysOff()
    {
        var result = new OpticalDiscManifestValidator().Parse(await TestFiles.ReadManifestAsync());

        await Assert.That(OpticalDiscManifestMapper.GetContentHash(result.Document!))
            .IsEqualTo("57B059114B517DF43BE4D05FCA0869FA");
        await Assert.That(OpticalDiscManifestMapper.GetGlobalDiscId(result.Document!))
            .IsEqualTo("DBB9FE8101D750F7BEB240B247213D91D3B2CFA9");
    }
}

public class DiscLogManifestComparerTests
{
    [Test]
    public async Task Compare_MissingSourceAndStreamSize_ReportWarningsWithoutChangingMismatch()
    {
        var manifest = CreateManifest(
            CreateTitle("BDMV/STREAM/00001.m2ts", chapters: 1, size: 100, lengthSeconds: 10, segmentMap: "1"));
        manifest.Disc!.Files.Add(new OpticalDiscManifestFile
        {
            Path = @"bdmv\stream\00001.m2ts",
            SizeBytes = 100
        });
        var log = CreateDiscInfo(
            CreateLogTitle("00001.m2ts", chapters: 1, size: 200, length: "0:00:10", segmentMap: "1"),
            CreateLogTitle("00002.mpls", chapters: 1, size: 100, length: "0:00:10", segmentMap: "2"));
        var result = DiscLogManifestComparer.Compare(
            log, string.Empty, manifest, OpticalDiscManifestMapper.ToDiscInfo(manifest), "HASH");

        await Assert.That(result.Status).IsEqualTo(DiscLogManifestComparisonStatus.Mismatch);
        await Assert.That(result.Differences.ArtifactWarnings.Count).IsEqualTo(2);
        await Assert.That(result.Differences.ArtifactWarnings.Any(w => w.Contains("00002.mpls"))).IsTrue();
        await Assert.That(result.Differences.ArtifactWarnings.Any(w => w.Contains("200 bytes"))).IsTrue();
        await Assert.That(result.Differences.DifferentDisc).IsFalse();
    }

    [Test]
    public async Task Compare_ScanDiagnostics_RoundTripIntoComparison()
    {
        var manifest = CreateManifest(
            CreateTitle("BDMV/PLAYLIST/00001.mpls", chapters: 1, size: 100, lengthSeconds: 10, segmentMap: "1"));
        manifest.Extensions = new()
        {
            ["thediscdb.optical-disc-manifest/scan-diagnostics"] =
                System.Text.Json.JsonSerializer.SerializeToElement(new[]
                {
                    new OpticalDiscScanDiagnostic
                    {
                        Code = "ODM_BD_CHAPTER_FINAL_MARK",
                        Severity = "info",
                        Path = "BDMV/PLAYLIST/00001.mpls",
                        Message = "distance from end 45045 ticks",
                        ByteOffset = 12
                    }
                }, OpticalDiscManifestDocument.SerializerOptions)
        };
        var result = DiscLogManifestComparer.Compare(
            CreateDiscInfo(CreateLogTitle("00001.mpls", chapters: 1, size: 100, length: "0:00:10", segmentMap: "1")),
            string.Empty, manifest, OpticalDiscManifestMapper.ToDiscInfo(manifest), "HASH");
        var persisted = System.Text.Json.JsonSerializer.Deserialize<DiscLogManifestDifferences>(
            result.DifferencesJson, OpticalDiscManifestDocument.SerializerOptions)!;

        await Assert.That(result.Status).IsEqualTo(DiscLogManifestComparisonStatus.Match);
        await Assert.That(persisted.ScanDiagnostics.Count).IsEqualTo(1);
        await Assert.That(persisted.ScanDiagnostics[0].ByteOffset).IsEqualTo(12L);
        await Assert.That(persisted.ArtifactWarnings).IsEmpty();
    }

    [Test]
    public async Task Compare_MalformedScanDiagnostics_ReportWarning()
    {
        var manifest = CreateManifest();
        manifest.Extensions = new()
        {
            ["thediscdb.optical-disc-manifest/scan-diagnostics"] =
                System.Text.Json.JsonSerializer.SerializeToElement("invalid")
        };
        var result = DiscLogManifestComparer.Compare(
            CreateDiscInfo(), string.Empty, manifest, CreateDiscInfo(), "HASH");

        await Assert.That(result.Differences.ArtifactWarnings.Count).IsEqualTo(1);
        await Assert.That(result.Differences.ScanDiagnostics).IsEmpty();
    }

    [Test]
    [Arguments("blu-ray", "00001.m2ts")]
    [Arguments("uhd-blu-ray", "00001.m2ts")]
    [Arguments("blu-ray", "00001.mpls(1)")]
    [Arguments("dvd-video", "1")]
    public async Task Compare_MatchingInventoryOrNoncanonicalSource_NoArtifactWarnings(string format, string source)
    {
        var manifest = CreateManifest();
        manifest.Disc!.Format = format;
        manifest.Disc.Files.Add(new OpticalDiscManifestFile
        {
            Path = @"bdmv\stream\00001.m2ts",
            SizeBytes = 100
        });
        var result = DiscLogManifestComparer.Compare(
            CreateDiscInfo(CreateLogTitle(source, chapters: 1, size: 100, length: "0:00:10", segmentMap: "1")),
            string.Empty, manifest, CreateDiscInfo(), "HASH");

        await Assert.That(result.Differences.ArtifactWarnings).IsEmpty();
    }

    [Test]
    [Arguments("null")]
    [Arguments("[null]")]
    [Arguments("[{}]")]
    public async Task Compare_InvalidDiagnosticEntries_ReportWarning(string diagnosticJson)
    {
        var manifest = CreateManifest();
        using var document = System.Text.Json.JsonDocument.Parse(diagnosticJson);
        manifest.Extensions = new()
        {
            ["thediscdb.optical-disc-manifest/scan-diagnostics"] = document.RootElement.Clone()
        };
        var result = DiscLogManifestComparer.Compare(
            CreateDiscInfo(), string.Empty, manifest, CreateDiscInfo(), "HASH");

        await Assert.That(result.Differences.ArtifactWarnings.Count).IsEqualTo(1);
        await Assert.That(result.Differences.ScanDiagnostics).IsEmpty();
    }

    [Test]
    public async Task Compare_SampleLogAndManifest_Matches()
    {
        string logText = await File.ReadAllTextAsync(TestFiles.LogPath);
        DiscInfo logInfo = LogParser.Organize(LogParser.Parse(logText.Split(Environment.NewLine)));
        var parsed = new OpticalDiscManifestValidator().Parse(await TestFiles.ReadManifestAsync());
        DiscInfo manifestInfo = OpticalDiscManifestMapper.ToDiscInfo(parsed.Document!);

        var result = DiscLogManifestComparer.Compare(
            logInfo,
            logText,
            parsed.Document!,
            manifestInfo,
            OpticalDiscManifestMapper.GetContentHash(parsed.Document!));

        await Assert.That(result.Status).IsEqualTo(DiscLogManifestComparisonStatus.Match);
        await Assert.That(result.LogTitleCount).IsEqualTo(result.ManifestTitleCount);
        await Assert.That(result.MatchedTitleCount).IsEqualTo(result.LogTitleCount);
        await Assert.That(result.OrderMatches).IsTrue();
    }

    [Test]
    public async Task Compare_ExtraLogTitle_IsMismatch()
    {
        var manifest = CreateManifest(
            CreateTitle("BDMV/PLAYLIST/00001.mpls", chapters: 1, size: 100, lengthSeconds: 10, segmentMap: "1"));
        DiscInfo manifestInfo = OpticalDiscManifestMapper.ToDiscInfo(manifest);
        DiscInfo logInfo = CreateDiscInfo(
            CreateLogTitle("00001.mpls", chapters: 1, size: 100, length: "0:00:10", segmentMap: "1"),
            CreateLogTitle("00002.mpls", chapters: 1, size: 100, length: "0:00:10", segmentMap: "2"));

        var result = DiscLogManifestComparer.Compare(logInfo, string.Empty, manifest, manifestInfo, "HASH");

        await Assert.That(result.Status).IsEqualTo(DiscLogManifestComparisonStatus.Mismatch);
        await Assert.That(result.Differences.LogOnlyTitles).Contains("00002.mpls");
    }

    [Test]
    public async Task Compare_DifferentChapterCount_IsMismatch()
    {
        var result = CompareSingleTitle(
            manifestTitle: CreateTitle("BDMV/PLAYLIST/00001.mpls", chapters: 3, size: 100, lengthSeconds: 10, segmentMap: "1"),
            logTitle: CreateLogTitle("00001.mpls", chapters: 2, size: 100, length: "0:00:10", segmentMap: "1"));

        await Assert.That(result.Status).IsEqualTo(DiscLogManifestComparisonStatus.Mismatch);
        await Assert.That(result.Differences.TitleDifferences.Any(d => d.Field == "ChapterCount")).IsTrue();
    }

    [Test]
    public async Task Compare_DifferentSize_IsMismatch()
    {
        var result = CompareSingleTitle(
            manifestTitle: CreateTitle("BDMV/PLAYLIST/00001.mpls", chapters: 1, size: 101, lengthSeconds: 10, segmentMap: "1"),
            logTitle: CreateLogTitle("00001.mpls", chapters: 1, size: 100, length: "0:00:10", segmentMap: "1"));

        await Assert.That(result.Status).IsEqualTo(DiscLogManifestComparisonStatus.Mismatch);
        await Assert.That(result.Differences.TitleDifferences.Any(d => d.Field == "SizeBytes")).IsTrue();
    }

    [Test]
    public async Task Compare_OrderSwap_DoesNotMakeMismatch()
    {
        var manifest = CreateManifest(
            CreateTitle("BDMV/PLAYLIST/00002.mpls", chapters: 1, size: 100, lengthSeconds: 10, segmentMap: "2"),
            CreateTitle("BDMV/PLAYLIST/00001.mpls", chapters: 1, size: 100, lengthSeconds: 10, segmentMap: "1"));
        DiscInfo manifestInfo = OpticalDiscManifestMapper.ToDiscInfo(manifest);
        DiscInfo logInfo = CreateDiscInfo(
            CreateLogTitle("00001.mpls", chapters: 1, size: 100, length: "0:00:10", segmentMap: "1"),
            CreateLogTitle("00002.mpls", chapters: 1, size: 100, length: "0:00:10", segmentMap: "2"));

        var result = DiscLogManifestComparer.Compare(logInfo, string.Empty, manifest, manifestInfo, "HASH");

        await Assert.That(result.Status).IsEqualTo(DiscLogManifestComparisonStatus.Match);
        await Assert.That(result.OrderMatches).IsFalse();
    }

    [Test]
    public async Task Compare_ZeroPaddedDvdTitleNumbers_Match()
    {
        var manifest = CreateManifest();
        DiscInfo manifestInfo = CreateDiscInfo(
            CreateLogTitle("1", chapters: 1, size: 100, length: "0:00:10", segmentMap: "1"),
            CreateLogTitle("10", chapters: 1, size: 100, length: "0:00:10", segmentMap: "2"));
        DiscInfo logInfo = CreateDiscInfo(
            CreateLogTitle("01", chapters: 1, size: 100, length: "0:00:10", segmentMap: "1"),
            CreateLogTitle("10", chapters: 1, size: 100, length: "0:00:10", segmentMap: "2"));

        var result = DiscLogManifestComparer.Compare(logInfo, string.Empty, manifest, manifestInfo, "HASH");

        await Assert.That(result.Status).IsEqualTo(DiscLogManifestComparisonStatus.Match);
        await Assert.That(result.Differences.LogOnlyTitles).IsEmpty();
        await Assert.That(result.Differences.ManifestOnlyTitles).IsEmpty();
    }

    [Test]
    public async Task Compare_DifferentContentHash_IsDifferentDisc()
    {
        var manifest = CreateManifest(
            CreateTitle("BDMV/PLAYLIST/00001.mpls", chapters: 1, size: 100, lengthSeconds: 10, segmentMap: "1"));
        DiscInfo manifestInfo = OpticalDiscManifestMapper.ToDiscInfo(manifest);
        DiscInfo logInfo = CreateDiscInfo(
            CreateLogTitle("00001.mpls", chapters: 1, size: 100, length: "0:00:10", segmentMap: "1"));

        var result = DiscLogManifestComparer.Compare(logInfo, string.Empty, manifest, manifestInfo, "OTHER");

        await Assert.That(result.Status).IsEqualTo(DiscLogManifestComparisonStatus.DifferentDisc);
        await Assert.That(result.Differences.DifferentDisc).IsTrue();
    }

    private static DiscLogManifestComparisonResult CompareSingleTitle(
        OpticalDiscManifestTitle manifestTitle,
        Title logTitle)
    {
        var manifest = CreateManifest(manifestTitle);
        return DiscLogManifestComparer.Compare(
            CreateDiscInfo(logTitle),
            string.Empty,
            manifest,
            OpticalDiscManifestMapper.ToDiscInfo(manifest),
            "HASH");
    }

    private static OpticalDiscManifestDocument CreateManifest(params OpticalDiscManifestTitle[] titles)
    {
        var manifest = new OpticalDiscManifestDocument
        {
            SchemaVersion = 1,
            Producer = new OpticalDiscManifestProducer
            {
                Name = "test",
                Version = "1.0.0"
            },
            Disc = new OpticalDiscManifestDisc
            {
                Format = "blu-ray",
                Identifiers =
                {
                    new OpticalDiscManifestIdentifier
                    {
                        Kind = OpticalDiscManifestIdentifier.ContentHashKind,
                        Value = "HASH"
                    }
                }
            }
        };

        foreach (var title in titles)
        {
            manifest.Disc.Titles.Add(title);
        }

        return manifest;
    }

    private static OpticalDiscManifestTitle CreateTitle(
        string path,
        int chapters,
        long size,
        double lengthSeconds,
        string segmentMap)
    {
        var title = new OpticalDiscManifestTitle
        {
            Source = new OpticalDiscManifestTitleSource { Path = path },
            ChapterCount = chapters,
            SizeBytes = size,
            DurationSeconds = lengthSeconds
        };

        foreach (string clip in segmentMap.Split(',', StringSplitOptions.RemoveEmptyEntries))
        {
            title.Segments.Add(new OpticalDiscManifestSegment { Clip = clip });
        }

        return title;
    }

    private static DiscInfo CreateDiscInfo(params Title[] titles)
    {
        var disc = new DiscInfo();
        foreach (var title in titles)
        {
            disc.Titles.Add(title);
        }

        return disc;
    }

    private static Title CreateLogTitle(string playlist, int chapters, long size, string length, string segmentMap)
        => new()
        {
            Playlist = playlist,
            ChapterCount = chapters,
            Size = size,
            Length = length,
            SegmentMap = segmentMap
        };
}

internal static class TestFiles
{
    public static string ManifestPath { get; } =
        Path.Combine(AppContext.BaseDirectory, "TestData", "sample-disc.odm.json");

    public static string LogPath { get; } =
        Path.Combine(AppContext.BaseDirectory, "TestData", "sample-disc-for-odm.txt");

    public static Task<string> ReadManifestAsync() => File.ReadAllTextAsync(ManifestPath);
}
