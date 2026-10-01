using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using TheDiscDb.OpticalDiscManifest.Generation;
using TheDiscDb.OpticalDiscManifest.Models;
using TheDiscDb.OpticalDiscParsers.Bdmv.Models;

namespace TheDiscDb.OpticalDiscParsers.Tests;

public sealed class OpticalDiscManifestGeneratorTests
{
    private readonly string fixturesPath = Path.Combine(AppContext.BaseDirectory, "fixtures");

    [Fact]
    public async Task GenerateAsync_BluRay_IsValidDeterministicAndDoesNotReadPayload()
    {
        var files = CreateBluRayFiles("BD-A");
        var request = CreateRequest(files, "aacs-disc-id", new string('A', 40));
        var generator = new OpticalDiscManifestGenerator();

        var first = await generator.GenerateAsync(request, TestContext.Current.CancellationToken);
        var second = await generator.GenerateAsync(request, TestContext.Current.CancellationToken);

        Assert.True(first.Validation.IsValid, string.Join(Environment.NewLine, first.Validation.Errors));
        Assert.Equal("blu-ray", first.Manifest.Disc.Format);
        Assert.Contains(first.Diagnostics, item => item.Code == "ODM_BD_TITLES_PARTIAL");
        Assert.Equal(1, first.Manifest.Disc.Identifiers.Count(item => item.Kind == "thediscdb-content-hash"));
        Assert.DoesNotContain("\"capabilities\"", System.Text.Encoding.UTF8.GetString(first.Json));
        Assert.DoesNotContain("\"diagnostics\"", System.Text.Encoding.UTF8.GetString(first.Json));
        Assert.NotEmpty(first.Manifest.Disc.Titles!);
        Assert.Equal(first.Json, second.Json);
        Assert.All(
            files.Where(file => file.Path.EndsWith(".m2ts", StringComparison.OrdinalIgnoreCase)),
            file => Assert.Equal(0, file.ReadCount));
    }

    [Fact]
    public void SchemaValidator_RejectsRemovedPropertiesAndMissingContentHash()
    {
        const string documentPrefix =
            """{"schemaVersion":1,"producer":{"name":"test","version":"1"},"disc":{"format":"unknown","identifiers":[{"kind":"thediscdb-content-hash","value":"0123456789ABCDEF0123456789ABCDEF"}],"files":[]""";
        var validator = new TheDiscDb.OpticalDiscManifest.Validation.OpticalDiscManifestSchemaValidator();
        string[] invalidDocuments =
        [
            """{"schemaVersion":1,"producer":{"name":"test","version":"1"},"capabilities":[],"disc":{"format":"unknown","identifiers":[{"kind":"thediscdb-content-hash","value":"0123456789ABCDEF0123456789ABCDEF"}],"files":[]}}""",
            documentPrefix + ""","titles":[{"source":{"title":1},"index":0}]}}""",
            """{"schemaVersion":1,"producer":{"name":"test","version":"1"},"disc":{"format":"unknown","files":[]}}""",
        ];

        Assert.All(invalidDocuments, json =>
            Assert.False(validator.Validate(Encoding.UTF8.GetBytes(json)).IsValid));
        Assert.True(validator.Validate(Encoding.UTF8.GetBytes(documentPrefix + "}}")).IsValid);
    }

    [Fact]
    public async Task GenerateAsync_ContentHashUsesOrdinalBareNamesAndExcludesBackupAndSsifFiles()
    {
        var files = new[]
        {
            RecordingFile.Payload("BDMV/STREAM/2.m2ts", 2),
            RecordingFile.Payload("BDMV/STREAM/10.M2TS", 1),
            RecordingFile.Payload("BDMV/BACKUP/STREAM/01.m2ts", 99),
            RecordingFile.Payload("BDMV/STREAM/00001.ssif", 77),
        };

        var result = await new OpticalDiscManifestGenerator().GenerateAsync(
            CreateRequest(files, "aacs-disc-id", new string('A', 40)),
            TestContext.Current.CancellationToken);

        Assert.True(result.Validation.IsValid, string.Join(Environment.NewLine, result.Validation.Errors));
        var contentHash = Assert.Single(
            result.Manifest.Disc.Identifiers,
            item => item.Kind == "thediscdb-content-hash");
        Span<byte> sizes = stackalloc byte[16];
        BinaryPrimitives.WriteInt64LittleEndian(sizes[..8], 1);
        BinaryPrimitives.WriteInt64LittleEndian(sizes[8..], 2);
        Assert.Equal(Convert.ToHexString(MD5.HashData(sizes)), contentHash.Value);
        Assert.All(files, file => Assert.Equal(0, file.ReadCount));
    }

    [Fact]
    public async Task GenerateAsync_BluRay_UsesDiscNameFromDlXml()
    {
        const string Xml =
            """<di:discinfo xmlns:di="urn:BDA:bdmv;discinfo"><di:title><di:name>Example Disc Name</di:name></di:title></di:discinfo>""";
        var xmlFile = RecordingFile.FromBytes(
            "BDMV/META/DL/bdmv_dl.xml",
            Encoding.UTF8.GetBytes(Xml));
        var files = CreateBluRayFiles("BD-A").Append(xmlFile).ToArray();

        var result = await new OpticalDiscManifestGenerator().GenerateAsync(
            CreateRequest(files, "aacs-disc-id", new string('A', 40)),
            TestContext.Current.CancellationToken);

        Assert.True(result.Validation.IsValid, string.Join(Environment.NewLine, result.Validation.Errors));
        Assert.Equal("Example Disc Name", result.Manifest.Disc.Name);
        Assert.Equal(1, xmlFile.ReadCount);
        Assert.All(
            files.Where(file => file.Path.EndsWith(".m2ts", StringComparison.OrdinalIgnoreCase)),
            file => Assert.Equal(0, file.ReadCount));
    }

    [Fact]
    public async Task GenerateAsync_BluRay_UsesBackupControlFileWhenPrimaryIsMissing()
    {
        var files = CreateBluRayFiles("BD-A")
            .Where(file => !file.Path.Equals("BDMV/index.bdmv", StringComparison.OrdinalIgnoreCase))
            .ToList();
        var indexSource = Path.Combine(fixturesPath, "BDMV", "BD-A", "index.bdmv");
        files.Add(RecordingFile.FromDisk("BDMV/BACKUP/index.bdmv", indexSource));
        var generator = new OpticalDiscManifestGenerator();

        var result = await generator.GenerateAsync(
            CreateRequest(files, "aacs-disc-id", new string('E', 40)),
            TestContext.Current.CancellationToken);

        Assert.True(result.Validation.IsValid, string.Join(Environment.NewLine, result.Validation.Errors));
        Assert.Equal("blu-ray", result.Manifest.Disc.Format);
        Assert.Contains(result.Manifest.Disc.Files, item => item.Path == "BDMV/BACKUP/index.bdmv");
        Assert.Contains(result.Diagnostics, item =>
            item.Code == "ODM_CONTROL_FILE_BACKUP_USED"
            && item.Path == "BDMV/BACKUP/index.bdmv");
        Assert.DoesNotContain(result.Diagnostics, item =>
            item.Code == "ODM_CONTROL_FILE_MISSING"
            && item.Path == "BDMV/index.bdmv");
    }

    [Fact]
    public async Task GenerateAsync_Uhd_UsesVersion0300Signal()
    {
        var files = CreateBluRayFiles("UHD-A");
        var generator = new OpticalDiscManifestGenerator();

        var result = await generator.GenerateAsync(
            CreateRequest(files, "aacs-disc-id", new string('B', 40)),
            TestContext.Current.CancellationToken);

        Assert.True(result.Validation.IsValid, string.Join(Environment.NewLine, result.Validation.Errors));
        Assert.Equal("uhd-blu-ray", result.Manifest.Disc.Format);
        Assert.NotEmpty(result.Manifest.Disc.Titles!);
    }

    [Fact]
    public async Task GenerateAsync_Uhd_SurfacesHevcDynamicRangeAndColorSpaceEvidence()
    {
        // Real UHD CLPI evidence (00589.clpi), already verified at the parser layer:
        // DynamicRangeTypeCode=1, ColorSpaceCode=2, HdrPlusFlag=false. This test proves the ODM
        // mapper surfaces that control-file-only HDR/color-space evidence into disc.clips instead
        // of silently dropping it.
        var files = CreateBluRayFiles("UHD-A")
            .Concat([RecordingFile.Payload("BDMV/STREAM/00589.m2ts", 1_000_000)])
            .ToList();
        var generator = new OpticalDiscManifestGenerator();

        var result = await generator.GenerateAsync(
            CreateRequest(files, "aacs-disc-id", new string('C', 40)),
            TestContext.Current.CancellationToken);

        Assert.True(result.Validation.IsValid, string.Join(Environment.NewLine, result.Validation.Errors));

        var clip = Assert.Single(result.Manifest.Disc.Clips!, item => item.ClipId == "00589");
        var hevcStream = Assert.Single(clip.Streams!, stream => stream.Pid == 0x1011);
        Assert.Equal(1, hevcStream.DynamicRangeTypeCode);
        Assert.Equal(2, hevcStream.ColorSpaceCode);
        Assert.False(hevcStream.HdrPlusFlag);
        Assert.Equal("video", hevcStream.Category);
        Assert.Equal("3840x2160", hevcStream.Resolution);
        Assert.Equal("16:9", hevcStream.AspectRatio);
        Assert.Equal(23.976, hevcStream.FrameRate);
        Assert.False(hevcStream.IsInterlaced);
        Assert.Contains(clip.Streams!, stream => stream.Type == "audio" && stream.SampleRate is not null);
        Assert.Contains(
            result.Manifest.Disc.Titles!.SelectMany(title => title.Streams ?? []),
            stream => stream.Category == "video"
                && stream.Resolution is not null
                && stream.FrameRate is not null
                && stream.IsInterlaced is not null);

        // Non-video streams must not fabricate HDR/color-space evidence they don't carry.
        Assert.All(
            clip.Streams!.Where(stream => stream.Type != "video"),
            stream =>
            {
                Assert.Null(stream.DynamicRangeTypeCode);
                Assert.Null(stream.ColorSpaceCode);
                Assert.Null(stream.HdrPlusFlag);
            });

        Assert.All(
            files.Where(file => file.Path.EndsWith(".m2ts", StringComparison.OrdinalIgnoreCase)),
            file => Assert.Equal(0, file.ReadCount));
    }

    [Fact]
    public async Task GenerateAsync_MvcPlaylist_MapsBaseAndDependentViewRelationshipAndClips()
    {
        // Combined base + dependent-view byte total per task evidence: 43,553,961,984.
        const long CombinedBytes = 43_553_961_984;
        const long PerClipBytes = CombinedBytes / 2;

        var files = new[]
        {
            RecordingFile.FromDisk(
                "BDMV/PLAYLIST/00800.mpls",
                Path.Combine(fixturesPath, "MPLS", "BD-3D", "00800.mpls")),
            RecordingFile.FromDisk(
                "BDMV/CLIPINF/00301.clpi",
                Path.Combine(fixturesPath, "CLPI", "BD-3D", "00301.clpi")),
            RecordingFile.Payload("BDMV/STREAM/00300.m2ts", PerClipBytes),
            RecordingFile.Payload("BDMV/STREAM/00301.m2ts", PerClipBytes),
        };
        var generator = new OpticalDiscManifestGenerator();
        var request = CreateRequest(files, "aacs-disc-id", new string('F', 40));

        var first = await generator.GenerateAsync(request, TestContext.Current.CancellationToken);
        var second = await generator.GenerateAsync(request, TestContext.Current.CancellationToken);

        Assert.True(first.Validation.IsValid, string.Join(Environment.NewLine, first.Validation.Errors));
        Assert.Equal(first.Json, second.Json);

        var title = Assert.Single(first.Manifest.Disc.Titles!);
        var stereoscopic3D = title.Stereoscopic3D;
        Assert.NotNull(stereoscopic3D);
        Assert.Equal("3d-dependent-view", stereoscopic3D!.RelationshipType);
        Assert.Equal("00300", stereoscopic3D.BaseClipId);
        Assert.Equal("00301", stereoscopic3D.DependentClipId);
        Assert.True(stereoscopic3D.IsSsVideoSubPath);
        Assert.NotNull(stereoscopic3D.DependentStream);
        Assert.Equal(0x1012, stereoscopic3D.DependentStream!.Pid);
        Assert.Equal(0x20, stereoscopic3D.DependentStream.CodingTypeCode);
        Assert.Equal("MVC Video", stereoscopic3D.DependentStream.Codec);
        Assert.Equal(6, stereoscopic3D.DependentStream.FormatCode);
        Assert.Equal(1, stereoscopic3D.DependentStream.RateCode);

        // Title size must account for both 00300 (base) and 00301 (dependent) without
        // reading any payload bytes -- only file-size metadata.
        Assert.Equal(CombinedBytes, title.SizeBytes);

        var clips = Assert.IsAssignableFrom<IReadOnlyList<ManifestClip>>(first.Manifest.Disc.Clips);
        Assert.Equal(2, clips.Count);

        var baseClip = Assert.Single(clips, clip => clip.ClipId == "00300");
        Assert.Equal("BDMV/STREAM/00300.m2ts", baseClip.StreamPath);
        Assert.Equal(PerClipBytes, Assert.Single(
            first.Manifest.Disc.Files,
            file => file.Path == baseClip.StreamPath).SizeBytes);
        // No CLPI fixture is available for 00300: CLPI-derived fields must be honestly
        // absent rather than inferred or guessed.
        Assert.Null(baseClip.ClipInfoPath);
        Assert.Null(baseClip.DurationSeconds);
        Assert.Null(baseClip.NumberOfSourcePackets);
        Assert.Null(baseClip.TransportStreamRecordingRate);
        Assert.Null(baseClip.Streams);

        var dependentClip = Assert.Single(clips, clip => clip.ClipId == "00301");
        Assert.Equal("BDMV/STREAM/00301.m2ts", dependentClip.StreamPath);
        Assert.Equal(PerClipBytes, Assert.Single(
            first.Manifest.Disc.Files,
            file => file.Path == dependentClip.StreamPath).SizeBytes);
        Assert.Equal("BDMV/CLIPINF/00301.clpi", dependentClip.ClipInfoPath);
        Assert.True(dependentClip.DurationSeconds > 0);
        Assert.True(dependentClip.NumberOfSourcePackets > 0);
        Assert.True(dependentClip.TransportStreamRecordingRate > 0);
        var mvcStream = Assert.Single(dependentClip.Streams!, stream => stream.CodingTypeCode == 0x20);
        Assert.Equal(0x1012, mvcStream.Pid);
        Assert.Equal("MVC Video", mvcStream.Codec);
        Assert.Equal(6, mvcStream.FormatCode);
        Assert.Equal(1, mvcStream.RateCode);

        // disc.clips are never opened; only control-file/CLPI evidence and file-size
        // metadata back them, per the CLPI/M2TS/SSIF payload no-read guarantee.
        Assert.All(
            files.Where(file => file.Path.EndsWith(".m2ts", StringComparison.OrdinalIgnoreCase)),
            file => Assert.Equal(0, file.ReadCount));
    }

    [Fact]
    public async Task GenerateAsync_BluRay_CollapsesDuplicatePlaylistsAndPromotesUnreferencedStreams()
    {
        // The same playlist authored twice is one playback candidate, and a stream file
        // no playlist reaches would otherwise never reach the identify flow at all.
        string fixture = Path.Combine(fixturesPath, "MPLS", "BD-B", "00050.mpls");
        var files = new[]
        {
            RecordingFile.FromDisk("BDMV/PLAYLIST/00050.mpls", fixture),
            RecordingFile.FromDisk("BDMV/PLAYLIST/00051.mpls", fixture),
            RecordingFile.Payload("BDMV/STREAM/00135.m2ts", 4096),
            RecordingFile.Payload("BDMV/STREAM/00007.m2ts", 8192),
            RecordingFile.Payload("BDMV/STREAM/00999.m2ts", 16384),
        };
        var generator = new OpticalDiscManifestGenerator();

        var result = await generator.GenerateAsync(
            CreateRequest(files, "aacs-disc-id", new string('3', 40)),
            TestContext.Current.CancellationToken);

        Assert.True(result.Validation.IsValid, string.Join(Environment.NewLine, result.Validation.Errors));
        var titles = result.Manifest.Disc.Titles!;
        Assert.Equal(2, titles.Count);

        // 00051 duplicates 00050 exactly, so only the first survives.
        Assert.Equal("BDMV/PLAYLIST/00050.mpls", titles[0].Source!.Path);
        var duplicate = Assert.Single(result.Diagnostics, item => item.Code == "ODM_BD_TITLE_DUPLICATE");
        Assert.Equal("BDMV/PLAYLIST/00051.mpls", duplicate.Path);
        Assert.Equal("info", duplicate.Severity);

        // 00135 and 00007 are reachable through the playlist; only 00999 is orphaned.
        var promoted = titles[1];
        Assert.Equal("BDMV/STREAM/00999.m2ts", promoted.Source!.Path);
        Assert.Equal(16384, promoted.SizeBytes);
        Assert.Equal("00999", Assert.Single(promoted.Segments!).Clip);
        Assert.Contains(result.Diagnostics, item => item.Code == "ODM_BD_STREAM_TITLES");

        // Promotion is backed by file-size metadata alone; payload bytes stay unread.
        Assert.All(
            files.Where(file => file.Path.EndsWith(".m2ts", StringComparison.OrdinalIgnoreCase)),
            file => Assert.Equal(0, file.ReadCount));
    }

    [Fact]
    public void CreateBluRayCompositionKey_DistinguishesTitlesThatPlayDifferently()
    {
        var baseline = CreateSegmentTitle(("00001", 0d, 10d, null));

        Assert.Equal(
            OpticalDiscManifestGenerator.CreateBluRayCompositionKey(baseline),
            OpticalDiscManifestGenerator.CreateBluRayCompositionKey(
                CreateSegmentTitle(("00001", 0d, 10d, null))));

        Assert.NotEqual(
            OpticalDiscManifestGenerator.CreateBluRayCompositionKey(baseline),
            OpticalDiscManifestGenerator.CreateBluRayCompositionKey(
                CreateSegmentTitle(("00002", 0d, 10d, null))));
        Assert.NotEqual(
            OpticalDiscManifestGenerator.CreateBluRayCompositionKey(baseline),
            OpticalDiscManifestGenerator.CreateBluRayCompositionKey(
                CreateSegmentTitle(("00001", 0d, 11d, null))));
        Assert.NotEqual(
            OpticalDiscManifestGenerator.CreateBluRayCompositionKey(baseline),
            OpticalDiscManifestGenerator.CreateBluRayCompositionKey(
                CreateSegmentTitle(("00001", 0d, 10d, 2))));
        Assert.NotEqual(
            OpticalDiscManifestGenerator.CreateBluRayCompositionKey(baseline),
            OpticalDiscManifestGenerator.CreateBluRayCompositionKey(
                CreateSegmentTitle(("00001", 0d, 10d, null), ("00002", 10d, 5d, null))));

        // Two playlists over the same clips still differ when their chapter marks do.
        var chaptered = baseline with
        {
            Chapters = [new ManifestChapter { StartSeconds = 0 }, new ManifestChapter { StartSeconds = 5 }],
        };
        Assert.NotEqual(
            OpticalDiscManifestGenerator.CreateBluRayCompositionKey(baseline),
            OpticalDiscManifestGenerator.CreateBluRayCompositionKey(chaptered));

        // Playlists over the same clips that select different audio (a commentary
        // variant, say) are distinct presentations.
        var english = baseline with
        {
            Streams = [new ManifestStream { Type = "audio", Codec = "ac3", Pid = 0x1100, Language = "eng" }],
        };
        var commentary = baseline with
        {
            Streams =
            [
                new ManifestStream { Type = "audio", Codec = "ac3", Pid = 0x1100, Language = "eng" },
                new ManifestStream { Type = "audio", Codec = "ac3", Pid = 0x1101, Language = "eng" },
            ],
        };
        Assert.NotEqual(
            OpticalDiscManifestGenerator.CreateBluRayCompositionKey(english),
            OpticalDiscManifestGenerator.CreateBluRayCompositionKey(commentary));
        Assert.Equal(
            OpticalDiscManifestGenerator.CreateBluRayCompositionKey(english),
            OpticalDiscManifestGenerator.CreateBluRayCompositionKey(english with { }));
    }

    [Theory]
    [InlineData(null, true)]
    [InlineData(12.012, true)]
    [InlineData(0.500489, true)]
    [InlineData(0.041689, false)]
    [InlineData(0d, false)]
    public void IsPlayableStreamCandidate_RejectsOnlySingleFrameStubs(double? duration, bool expected)
    {
        // 0.041689s is one frame at 23.976fps: BD-J filler, not a playback candidate.
        Assert.Equal(expected, OpticalDiscManifestGenerator.IsPlayableStreamCandidate(duration));
    }

    [Fact]
    public void AttributeSingleClipTitleToStream_UsesStreamPathOnlyWhenThePlaylistAddsNothing()
    {
        IReadOnlySet<string> streams = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "BDMV/STREAM/00001.m2ts",
            "BDMV/STREAM/00002.m2ts",
            "BDMV/STREAM/00003.m2ts",
        };
        var clipDurations = new Dictionary<string, double>
        {
            ["00001"] = 10.0,
            ["00002"] = 15.0,
            ["00003"] = 93.460022,
        };
        Func<string, double?> durationOf = clip => clipDurations.TryGetValue(clip, out var value) ? value : null;

        var single = CreateSegmentTitle(("00001", 0d, 10d, null));
        Assert.Equal(
            "BDMV/STREAM/00001.m2ts",
            OpticalDiscManifestGenerator.AttributeSingleClipTitleToStream(single, streams, durationOf).Source!.Path);

        // A lone chapter mark is just the title's start, so it still adds nothing, and a
        // stream-sourced title carries no chapters, as in MakeMKV.
        var oneChapter = single with { Chapters = [new ManifestChapter { StartSeconds = 0 }] };
        var attributed = OpticalDiscManifestGenerator.AttributeSingleClipTitleToStream(oneChapter, streams, durationOf);
        Assert.Equal("BDMV/STREAM/00001.m2ts", attributed.Source!.Path);
        Assert.Null(attributed.Chapters);

        // Everything the playlist genuinely contributes keeps the playlist attribution.
        var chaptered = single with
        {
            Chapters = [new ManifestChapter { StartSeconds = 0 }, new ManifestChapter { StartSeconds = 5 }],
        };
        var multiSegment = CreateSegmentTitle(("00001", 0d, 10d, null), ("00002", 10d, 5d, null));
        var angled = CreateSegmentTitle(("00001", 0d, 10d, 1));
        var missingStream = CreateSegmentTitle(("00777", 0d, 10d, null));
        // Mirrors a real disc: the playlist plays 89.089s of a 93.46s clip, and MakeMKV
        // keeps the playlist name for it.
        var trimmed = CreateSegmentTitle(("00003", 0d, 89.089, null));
        var unknownClipDuration = CreateSegmentTitle(("00002", 0d, 15d, null));
        foreach (var title in new[] { chaptered, multiSegment, angled, missingStream, trimmed })
        {
            Assert.Equal(
                "BDMV/PLAYLIST/00050.mpls",
                OpticalDiscManifestGenerator.AttributeSingleClipTitleToStream(title, streams, durationOf).Source!.Path);
        }

        Assert.Equal(
            "BDMV/PLAYLIST/00050.mpls",
            OpticalDiscManifestGenerator.AttributeSingleClipTitleToStream(unknownClipDuration, streams, _ => null).Source!.Path);
    }

    [Fact]
    public void OrderBluRayTitles_PlacesPlaylistTitlesBeforeStreamTitles()
    {
        static ManifestTitle At(string path) => new() { Source = new ManifestTitleSource { Path = path } };

        var ordered = OpticalDiscManifestGenerator.OrderBluRayTitles(
        [
            At("BDMV/PLAYLIST/00004.mpls"),
            At("BDMV/STREAM/00012.m2ts"),
            At("BDMV/PLAYLIST/00000.mpls"),
            At("BDMV/STREAM/00002.m2ts"),
            At("BDMV/PLAYLIST/00006.mpls"),
        ]);

        // Playlist titles keep their incoming order; stream titles follow in clip order.
        Assert.Equal(
            [
                "BDMV/PLAYLIST/00004.mpls",
                "BDMV/PLAYLIST/00000.mpls",
                "BDMV/PLAYLIST/00006.mpls",
                "BDMV/STREAM/00002.m2ts",
                "BDMV/STREAM/00012.m2ts",
            ],
            ordered.Select(title => title.Source!.Path));
    }

    [Fact]
    public async Task GenerateAsync_BluRay_MatchesMakeMkvTitleListForHellOnWheelsDisc()
    {
        // Real control files from Hell on Wheels Season 1 Disc 1 (fixture set BD-C),
        // compared with a MakeMKV 1.18.3 log of the same disc. MakeMKV lists exactly these
        // titles, with these chapter counts and h:mm:ss lengths (partial seconds truncated).
        (string Clip, long Size)[] streams =
        [
            ("00000", 7910012928), ("00001", 15003648), ("00002", 5812224), ("00003", 5407795200),
            ("00004", 5895487488), ("00005", 7580424192), ("00006", 110592), ("00007", 1013760),
            ("00008", 119734272), ("00009", 2328576), ("00010", 6936576), ("00011", 405504),
            ("00012", 4294656), ("00013", 243701760), ("00014", 246521856), ("00015", 61022208),
            ("00016", 2476032), ("00017", 24576), ("00018", 325570560),
        ];
        var files = new List<RecordingFile>();
        AddDirectory(files, Path.Combine(fixturesPath, "MPLS", "BD-C"), "BDMV/PLAYLIST");
        AddDirectory(files, Path.Combine(fixturesPath, "CLPI", "BD-C"), "BDMV/CLIPINF");
        files.AddRange(streams.Select(stream => RecordingFile.Payload($"BDMV/STREAM/{stream.Clip}.m2ts", stream.Size)));
        var generator = new OpticalDiscManifestGenerator();

        var result = await generator.GenerateAsync(
            CreateRequest(files, "aacs-disc-id", new string('4', 40)),
            TestContext.Current.CancellationToken);

        Assert.True(result.Validation.IsValid, string.Join(Environment.NewLine, result.Validation.Errors));
        var actual = result.Manifest.Disc.Titles!
            .Select(title =>
            {
                string name = Path.GetFileName(title.Source!.Path!);
                if (title.Source.Part is > 0)
                {
                    name += $"({title.Source.Part})";
                }

                return (name, title.Chapters?.Count ?? 0, (int)Math.Floor(title.DurationSeconds!.Value), title.SizeBytes);
            })
            .ToArray();

        Assert.Equal<(string, int, int, long?)>(
            [
                ("00000.mpls", 6, 2535, 5407795200),
                ("00004.mpls", 6, 2694, 7910012928),
                ("00005.mpls(1)", 2, 92, 243701760),
                ("00006.mpls", 2, 93, 246521856),
                ("00012.mpls", 6, 2571, 7580424192),
                ("00013.mpls", 6, 2557, 5895487488),
                ("00001.m2ts", 0, 15, 15003648),
                ("00002.m2ts", 0, 12, 5812224),
                ("00006.m2ts", 0, 0, 110592),
                ("00007.m2ts", 0, 5, 1013760),
                ("00008.m2ts", 0, 30, 119734272),
                ("00010.m2ts", 0, 9, 6936576),
                ("00011.m2ts", 0, 0, 405504),
                ("00012.m2ts", 0, 7, 4294656),
                ("00015.m2ts", 0, 80, 61022208),
                ("00017.m2ts", 0, 1, 24576),
                ("00018.m2ts", 0, 100, 325570560),
            ],
            actual);
    }

    [Fact]
    public void SplitBluRayPlaylist_SplitsOnlyAtNonSeamlessConnections()
    {
        var playlist = CreateMultiItemPlaylist(
            [("00010", 1, 0), ("00011", 5, 0), ("00013", 1, 0)],
            [(0, 0u), (1, 0u), (2, 0u), (2, 900u)]);

        var parts = OpticalDiscManifestGenerator.SplitBluRayPlaylist(playlist);

        Assert.Equal(2, parts.Count);
        Assert.Equal(["00010", "00011"], parts[0].PlayItems.Select(item => item.ClipId));
        Assert.Equal([0, 1], parts[0].PlayItems.Select(item => item.Index));
        Assert.Equal([0, 1], parts[0].Marks.Select(mark => mark.PlayItemReference));
        Assert.Equal(["00013"], parts[1].PlayItems.Select(item => item.ClipId));
        Assert.Equal([0], parts[1].PlayItems.Select(item => item.Index));
        Assert.Equal([0, 0], parts[1].Marks.Select(mark => mark.PlayItemReference));
        Assert.Equal([0, 1], parts[1].Marks.Select(mark => mark.Index));
    }

    [Fact]
    public void SplitBluRayPlaylist_KeepsSeamlessAndStillPlaylistsWhole()
    {
        var seamless = CreateMultiItemPlaylist([("00010", 1, 0), ("00011", 6, 0)], [(0, 0u)]);
        // Mirrors Avengers: Age of Ultron's 00050.mpls, which MakeMKV lists as one title.
        var stills = CreateMultiItemPlaylist([("00135", 1, 2), ("00007", 1, 2)], [(0, 0u), (1, 0u)]);

        Assert.Same(seamless, Assert.Single(OpticalDiscManifestGenerator.SplitBluRayPlaylist(seamless)));
        Assert.Same(stills, Assert.Single(OpticalDiscManifestGenerator.SplitBluRayPlaylist(stills)));
    }

    private static MplsPlaylist CreateMultiItemPlaylist(
        IReadOnlyList<(string Clip, int ConnectionCondition, int StillMode)> items,
        IReadOnlyList<(int PlayItem, uint Time)> marks)
    {
        var template = CreateSyntheticPlaylist(45_000, []);
        var playItem = template.PlayItems[0];
        return template with
        {
            PlayItems = items
                .Select((item, index) => playItem with
                {
                    Index = index,
                    ClipId = item.Clip,
                    ConnectionCondition = item.ConnectionCondition,
                    StillMode = item.StillMode,
                    Clips = [new MplsClipReference { ClipId = item.Clip, CodecId = "M2TS", StcId = 0 }],
                })
                .ToArray(),
            Marks = marks
                .Select((mark, index) => new MplsPlaylistMark
                {
                    Index = index,
                    MarkType = MplsPlaylistMark.EntryMarkType,
                    PlayItemReference = mark.PlayItem,
                    Time = mark.Time,
                    EntryElementaryStreamPid = 0,
                    Duration = 0,
                })
                .ToArray(),
        };
    }

    private static ManifestTitle CreateSegmentTitle(
        params (string Clip, double Start, double Duration, int? Angle)[] segments)
        => new()
        {
            Source = new ManifestTitleSource { Path = "BDMV/PLAYLIST/00050.mpls" },
            Segments = segments
                .Select(segment => new ManifestSegment
                {
                    Clip = segment.Clip,
                    StartSeconds = segment.Start,
                    DurationSeconds = segment.Duration,
                    Angle = segment.Angle,
                })
                .ToArray(),
        };

    /// <summary>
    /// Real long-playlist evidence captured from a mounted disc and cross-checked against its
    /// stored manifest. Clip byte sizes and segment ordering are retained as regression data;
    /// only the clip payload bytes are synthesized because the parser must never read them.
    /// </summary>
    private static readonly IReadOnlyDictionary<string, long> LongPlaylistClipSizes = new Dictionary<string, long>
    {
        ["00062"] = 5905029120L,
        ["00069"] = 3544399872L,
        ["00070"] = 129718272L,
        ["00071"] = 16803827712L,
        ["00072"] = 257193984L,
        ["00073"] = 717262848L,
        ["00074"] = 588361728L,
        ["00075"] = 334688256L,
        ["00076"] = 363061248L,
        ["00077"] = 80590848L,
        ["00078"] = 4780916736L,
        ["00079"] = 94113792L,
        ["00080"] = 873486336L,
        ["00081"] = 230240256L,
        ["00082"] = 2554140672L,
        ["00083"] = 269598720L,
        ["00084"] = 5606166528L,
        ["00085"] = 121092096L,
        ["00086"] = 3854635008L,
        ["00087"] = 230019072L,
        ["00088"] = 645347328L,
        ["00089"] = 90605568L,
        ["00090"] = 279035904L,
        ["00091"] = 118407168L,
        ["00092"] = 2760923136L,
        ["00093"] = 210868224L,
        ["00094"] = 636125184L,
        ["00095"] = 116459520L,
        ["00096"] = 439842816L,
        ["00097"] = 252493824L,
        ["00098"] = 831375360L,
        ["00099"] = 111728640L,
        ["00100"] = 2473943040L,
        ["00101"] = 278476800L,
        ["00102"] = 6244890624L,
        ["00103"] = 125024256L,
        ["00104"] = 971065344L,
        ["00105"] = 8711098368L,
        ["00106"] = 13898870784L,
        ["00107"] = 129589248L,
        ["00108"] = 256739328L,
        ["00109"] = 588158976L,
        ["00110"] = 80584704L,
        ["00111"] = 94083072L,
        ["00112"] = 230719488L,
        ["00113"] = 269457408L,
        ["00114"] = 121067520L,
        ["00115"] = 230191104L,
        ["00116"] = 90519552L,
        ["00117"] = 118628352L,
        ["00118"] = 210714624L,
        ["00119"] = 116496384L,
        ["00120"] = 252499968L,
        ["00121"] = 111642624L,
        ["00122"] = 278673408L,
        ["00123"] = 125128704L,
        ["00124"] = 8710748160L,
        ["00125"] = 363356160L,
    };

    private static readonly string[] LongPlaylist00800SegmentMap =
    [
        "00062", "00107", "00071", "00108", "00073", "00109", "00075", "00125", "00069", "00110",
        "00078", "00111", "00080", "00112", "00082", "00113", "00084", "00114", "00086", "00115",
        "00088", "00116", "00090", "00117", "00092", "00118", "00094", "00119", "00096", "00120",
        "00098", "00121", "00100", "00122", "00102", "00123", "00104", "00124", "00106",
    ];

    private static readonly string[] LongPlaylist00801SegmentMap =
    [
        "00062", "00070", "00071", "00072", "00073", "00074", "00075", "00076", "00069", "00077",
        "00078", "00079", "00080", "00081", "00082", "00083", "00084", "00085", "00086", "00087",
        "00088", "00089", "00090", "00091", "00092", "00093", "00094", "00095", "00096", "00097",
        "00098", "00099", "00100", "00101", "00102", "00103", "00104", "00105", "00106",
    ];

    [Theory]
    [InlineData("00800.mpls", 86_534_971_392L)]
    [InlineData("00801.mpls", 86_535_124_992L)]
    public async Task GenerateAsync_LongPlaylist_MatchesSegmentOrderSizeDurationAndCorrectedChapterCount(
        string playlistFileName,
        long expectedSizeBytes)
    {
        var files = new List<RecordingFile>
        {
            RecordingFile.FromDisk(
                $"BDMV/PLAYLIST/{playlistFileName}",
                Path.Combine(fixturesPath, "MPLS", "UHD-B", playlistFileName)),
        };
        foreach (var (clipId, size) in LongPlaylistClipSizes)
        {
            files.Add(RecordingFile.FromDisk(
                $"BDMV/CLIPINF/{clipId}.clpi",
                Path.Combine(fixturesPath, "CLPI", "UHD-A", "00589.clpi")));
            files.Add(RecordingFile.Payload($"BDMV/STREAM/{clipId}.m2ts", size));
        }

        var generator = new OpticalDiscManifestGenerator();
        var request = CreateRequest(files, "aacs-disc-id", new string('D', 40));

        var first = await generator.GenerateAsync(request, TestContext.Current.CancellationToken);
        var second = await generator.GenerateAsync(request, TestContext.Current.CancellationToken);

        Assert.True(first.Validation.IsValid, string.Join(Environment.NewLine, first.Validation.Errors));
        Assert.Equal(first.Json, second.Json);

        var title = Assert.Single(
            first.Manifest.Disc.Titles!,
            candidate => candidate.Source.Path == $"BDMV/PLAYLIST/{playlistFileName}");

        // Exact whole-file byte sum from the real disc / stored disc02.json fixture.
        Assert.Equal(expectedSizeBytes, title.SizeBytes);
        var expectedSegmentMapForSizeCheck = playlistFileName == "00800.mpls"
            ? LongPlaylist00800SegmentMap
            : LongPlaylist00801SegmentMap;
        Assert.Equal(
            expectedSegmentMapForSizeCheck.Sum(clip => LongPlaylistClipSizes[clip]),
            title.SizeBytes);

        // Real-disc duration evidence (9722.003467s / 437,490,156 ticks @ 45kHz).
        Assert.Equal(9722.003467, title.DurationSeconds);

        // Exact 39-segment ordering per the stored disc02.json SegmentMap.
        var expectedSegmentMap = playlistFileName == "00800.mpls"
            ? LongPlaylist00800SegmentMap
            : LongPlaylist00801SegmentMap;
        Assert.Equal(39, expectedSegmentMap.Length);
        Assert.Equal(expectedSegmentMap, title.Segments!.Select(segment => segment.Clip).ToArray());

        // Raw entry marks are 36; the corrected/emitted chapter count excludes the terminal
        // chapter-end sentinel, per task evidence (final mark 11,261 ticks from playlist end).
        Assert.Equal(35, title.Chapters!.Count);

        Assert.Contains(
            first.Diagnostics,
            item => item.Code == "ODM_BD_CHAPTER_TERMINAL_SENTINEL_EXCLUDED"
                && item.Path == $"BDMV/PLAYLIST/{playlistFileName}"
                && item.Message.Contains("11261"));

        // The real MPLS extension type/version (3.5) is preserved as a truthful,
        // unsupported-entry diagnostic rather than guessed at in the mapper.
        Assert.Contains(
            first.Diagnostics,
            item => item.Code == "ODM_BD_EXTENSION_UNSUPPORTED"
                && item.Path == $"BDMV/PLAYLIST/{playlistFileName}"
                && item.Message.Contains("3.5"));
        string unsupportedExtensionEvidence =
            title.Extensions!["thediscdb.optical-disc-manifest/unsupported-mpls-extensions"].GetRawText();
        Assert.Contains("\"typeIdentifier\":3", unsupportedExtensionEvidence);
        Assert.Contains("\"versionIdentifier\":5", unsupportedExtensionEvidence);

        // Never open CLPI/M2TS payloads: only file-size metadata backs disc.clips/segments.
        Assert.All(
            files.Where(file => file.Path.EndsWith(".m2ts", StringComparison.OrdinalIgnoreCase)),
            file => Assert.Equal(0, file.ReadCount));
    }

    [Fact]
    public async Task GenerateAsync_Dvd_JoinsLogicalTitlesAndChapterTiming()
    {
        var root = Path.Combine(fixturesPath, "DVD-A");
        var files = Directory.GetFiles(root, "*.IFO")
            .Select(path => RecordingFile.FromDisk(
                $"VIDEO_TS/{Path.GetFileName(path)}",
                path))
            .Append(RecordingFile.Payload("VIDEO_TS/VTS_01_1.VOB", 1_000_000))
            .ToArray();
        var generator = new OpticalDiscManifestGenerator();

        var result = await generator.GenerateAsync(
            CreateRequest(files, "dvd-disc-id", new string('C', 32)),
            TestContext.Current.CancellationToken);

        Assert.True(result.Validation.IsValid, string.Join(Environment.NewLine, result.Validation.Errors));
        Assert.Equal("dvd", result.Manifest.Disc.Format);
        Assert.DoesNotContain(
            result.Diagnostics,
            item => item.Code == "ODM_DVD_PARTIAL");

        var titles = Assert.IsAssignableFrom<IReadOnlyList<ManifestTitle>>(result.Manifest.Disc.Titles);
        Assert.Equal(23, titles.Count);
        var mainTitle = titles[0];
        Assert.Equal(1, mainTitle.Source.Title);
        Assert.Equal(1, mainTitle.Source.TitleSet);
        Assert.Equal(1, mainTitle.Source.TitleSetTitle);
        Assert.Equal(35, mainTitle.Chapters!.Count);
        Assert.Equal(0, mainTitle.Chapters[0].StartSeconds);
        Assert.True(mainTitle.Chapters[0].DurationSeconds > 0);
        Assert.True(mainTitle.DurationSeconds > 0);
        Assert.Equal(4_459_864_064L, mainTitle.SizeBytes);
        Assert.Equal(0, files.Single(file => file.Path.EndsWith(".VOB", StringComparison.OrdinalIgnoreCase)).ReadCount);
    }

    [Fact]
    public async Task GenerateAsync_DvdSequentialTitles_SumAuthoredCellSectorsToMakeMkvTitleSizes()
    {
        // MakeMKV TINFO:11 sizes from the associated multi-title DVD fixture baseline.
        long[] expected =
        [
            4_774_885_376, 141_600_768, 86_566_912, 78_424_064, 29_485_056, 42_805_248,
            378_882_048, 209_395_712, 198_991_872, 351_432_704, 185_497_600, 271_239_168,
            454_447_104, 1_671_004_160, 48_924_672, 11_198_464, 37_392_384,
        ];
        var files = CreateDvdIfoFiles("DVD-B");

        var result = await new OpticalDiscManifestGenerator().GenerateAsync(
            CreateRequest(files, "dvd-disc-id", new string('E', 32)),
            TestContext.Current.CancellationToken);

        Assert.True(result.Validation.IsValid, string.Join(Environment.NewLine, result.Validation.Errors));
        var titles = result.Manifest.Disc.Titles!;
        Assert.Equal(expected, titles.Select(title => title.SizeBytes ?? -1).ToArray());
        Assert.All(files.Where(file => file.Path.EndsWith(".VOB", StringComparison.OrdinalIgnoreCase)), file => Assert.Equal(0, file.ReadCount));
    }

    [Fact]
    public async Task GenerateAsync_Dvd_SizesSequentialMainFeatureAndOmitsMultiPgcSizes()
    {
        var files = CreateDvdIfoFiles("DVD-D");

        var result = await new OpticalDiscManifestGenerator().GenerateAsync(
            CreateRequest(files, "dvd-disc-id", new string('F', 32)),
            TestContext.Current.CancellationToken);

        Assert.True(result.Validation.IsValid, string.Join(Environment.NewLine, result.Validation.Errors));
        var titles = result.Manifest.Disc.Titles!;
        Assert.Equal(6, titles.Count);

        var main = titles[0];
        Assert.Equal(2, main.Source.TitleSet);
        Assert.Equal(37, main.Chapters!.Count);
        Assert.Equal(8342.527528, main.DurationSeconds);
        // Exactly MakeMKV's title size and every sector of VTS_02_1..8.VOB (all 59 cells).
        Assert.Equal(7_932_198_912L, main.SizeBytes);

        // VTS_V_ATR 0x4E 0x80: NTSC 16:9 with line-21 CC field 1 (MakeMKV: 16:9 + CC608 track).
        var video = Assert.Single(main.Streams!, stream => stream.Type == "video");
        Assert.Equal("720x480", video.Resolution);
        Assert.Equal("16:9", video.AspectRatio);
        Assert.Equal("video", video.Category);
        Assert.Equal(29.97, video.FrameRate);
        Assert.Equal([1], video.Line21ClosedCaptionFields);
        Assert.All(main.Streams!.Where(stream => stream.Type == "subtitle"), stream => Assert.Equal("RLE", stream.Codec));
        Assert.Contains("\"line21ClosedCaptionFields\":[1]", System.Text.RegularExpressions.Regex.Replace(System.Text.Encoding.UTF8.GetString(result.Json), @"\s", string.Empty));

        // Multi/random-PGC titles keep counts but omit timing and size rather than guessing.
        Assert.All(titles.Skip(1), title =>
        {
            Assert.Null(title.DurationSeconds);
            Assert.Null(title.SizeBytes);
        });
        Assert.All(files.Where(file => file.Path.EndsWith(".VOB", StringComparison.OrdinalIgnoreCase)), file => Assert.Equal(0, file.ReadCount));
    }

    private RecordingFile[] CreateDvdIfoFiles(string fixture)
        => Directory.GetFiles(Path.Combine(fixturesPath, fixture), "*.IFO")
            .Select(path => RecordingFile.FromDisk($"VIDEO_TS/{Path.GetFileName(path)}", path))
            .Append(RecordingFile.Payload("VIDEO_TS/VTS_01_1.VOB", 1_000_000))
            .ToArray();

    [Fact]
    public async Task GenerateAsync_DvdMissingTitleSet_DoesNotClaimCompleteTitles()
    {
        var root = Path.Combine(fixturesPath, "DVD-A");
        var files = Directory.GetFiles(root, "*.IFO")
            .Where(path => !path.EndsWith("VTS_02_0.IFO", StringComparison.OrdinalIgnoreCase))
            .Select(path => RecordingFile.FromDisk(
                $"VIDEO_TS/{Path.GetFileName(path)}",
                path))
            .ToArray();
        var generator = new OpticalDiscManifestGenerator();

        var result = await generator.GenerateAsync(
            CreateRequest(files, "dvd-disc-id", new string('D', 32)),
            TestContext.Current.CancellationToken);

        Assert.True(result.Validation.IsValid, string.Join(Environment.NewLine, result.Validation.Errors));
        Assert.Contains(result.Diagnostics, item => item.Code == "ODM_DVD_PARTIAL");
    }

    [Fact]
    public void CreateBluRayChapters_OnlyEmitsEntryMarksAsChapters()
    {
        var diagnostics = new List<ManifestDiagnostic>();
        var playlist = CreateSyntheticPlaylist(
            outTime: 200_000,
            marks:
            [
                (0, MplsPlaylistMark.EntryMarkType, 0, 45_000),
                (1, MplsPlaylistMark.LinkMarkType, 20_000, 0),
                (2, MplsPlaylistMark.EntryMarkType, 100_000, 45_000),
            ]);

        var chapters = OpticalDiscManifestGenerator.CreateBluRayChapters(
            playlist, diagnostics, "BDMV/PLAYLIST/00000.mpls");

        Assert.Equal(2, chapters.Count);
        Assert.Equal(0, chapters[0].StartSeconds);
        Assert.Equal(2.222222, chapters[1].StartSeconds);
    }

    [Theory]
    [InlineData(1_876)] // One-frame terminal marks from short titles.
    [InlineData(11_261)] // Measured terminal-mark offset.
    [InlineData(11_262)] // Measured terminal-mark offset.
    [InlineData(15_014)] // Measured terminal-mark offset.
    [InlineData(22_500)] // Upper bound: exactly one half second.
    public void CreateBluRayChapters_ExcludesFinalEntryMarkAtKnownTerminalSentinelTicks(long ticksFromEnd)
    {
        var diagnostics = new List<ManifestDiagnostic>();
        const long TotalDurationTicks = 200_000;
        long finalMarkStart = TotalDurationTicks - ticksFromEnd;
        var playlist = CreateSyntheticPlaylist(
            outTime: TotalDurationTicks,
            marks:
            [
                (0, MplsPlaylistMark.EntryMarkType, 0, 45_000),
                (1, MplsPlaylistMark.EntryMarkType, (uint)finalMarkStart, 45_000),
            ]);

        var chapters = OpticalDiscManifestGenerator.CreateBluRayChapters(
            playlist, diagnostics, "BDMV/PLAYLIST/00000.mpls");

        var chapter = Assert.Single(chapters);
        Assert.Equal(0, chapter.StartSeconds);
        Assert.Contains(
            diagnostics,
            item => item.Code == "ODM_BD_CHAPTER_TERMINAL_SENTINEL_EXCLUDED" && item.Severity == "info");
    }

    [Theory]
    [InlineData(0)] // A mark exactly at playlist end is not strictly before end.
    [InlineData(22_501)] // Just above the half-second band.
    [InlineData(45_000)] // A clearly legitimate final chapter, about 1 second from end.
    public void CreateBluRayChapters_DoesNotExcludeLegitimateFinalChapterOutsideSentinelTolerance(
        long ticksFromEnd)
    {
        var diagnostics = new List<ManifestDiagnostic>();
        const long TotalDurationTicks = 200_000;
        long finalMarkStart = TotalDurationTicks - ticksFromEnd;
        var playlist = CreateSyntheticPlaylist(
            outTime: TotalDurationTicks,
            marks:
            [
                (0, MplsPlaylistMark.EntryMarkType, 0, 45_000),
                (1, MplsPlaylistMark.EntryMarkType, (uint)finalMarkStart, 45_000),
            ]);

        var chapters = OpticalDiscManifestGenerator.CreateBluRayChapters(
            playlist, diagnostics, "BDMV/PLAYLIST/00000.mpls");

        Assert.Equal(2, chapters.Count);
        Assert.Equal(Math.Round(finalMarkStart / 45_000d, 6), chapters[1].StartSeconds);
        Assert.DoesNotContain(
            diagnostics,
            item => item.Code == "ODM_BD_CHAPTER_TERMINAL_SENTINEL_EXCLUDED");
    }

    [Fact]
    public async Task GenerateAsync_ShortPlaylist_PreservesLegitimateSecondChapter()
    {
        var playlist = RecordingFile.FromDisk(
            "BDMV/PLAYLIST/00050.mpls",
            Path.Combine(fixturesPath, "MPLS", "BD-B", "00050.mpls"));
        var generator = new OpticalDiscManifestGenerator();

        var result = await generator.GenerateAsync(
            CreateRequest([playlist], "aacs-disc-id", new string('2', 40)),
            TestContext.Current.CancellationToken);

        Assert.True(result.Validation.IsValid);
        var title = Assert.Single(result.Manifest.Disc.Titles!);
        Assert.Equal(2, title.Chapters!.Count);
        Assert.Equal([0d, 0.0834], title.Chapters.Select(chapter => chapter.StartSeconds));
        Assert.DoesNotContain(
            result.Diagnostics,
            item => item.Code == "ODM_BD_CHAPTER_TERMINAL_SENTINEL_EXCLUDED");
        Assert.Equal(1, playlist.ReadCount);
    }

    [Fact]
    public void CreateBluRayChapters_OnlyChecksSentinelToleranceAgainstTheFinalMark()
    {
        // A non-final mark that lands within the sentinel tolerance band, purely by
        // coincidence relative to a *later* mark's distance-from-end, must never be
        // excluded: only the actual final entry mark is checked.
        var diagnostics = new List<ManifestDiagnostic>();
        const long TotalDurationTicks = 200_000;
        const long CoincidentalSentinelLikeStart = TotalDurationTicks - 11_261;
        var playlist = CreateSyntheticPlaylist(
            outTime: TotalDurationTicks,
            marks:
            [
                (0, MplsPlaylistMark.EntryMarkType, 0, 45_000),
                (1, MplsPlaylistMark.EntryMarkType, (uint)CoincidentalSentinelLikeStart, 45_000),
                (2, MplsPlaylistMark.EntryMarkType, (uint)(TotalDurationTicks - 45_000), 45_000),
            ]);

        var chapters = OpticalDiscManifestGenerator.CreateBluRayChapters(
            playlist, diagnostics, "BDMV/PLAYLIST/00000.mpls");

        Assert.Equal(3, chapters.Count);
        Assert.DoesNotContain(
            diagnostics,
            item => item.Code == "ODM_BD_CHAPTER_TERMINAL_SENTINEL_EXCLUDED");
    }

    [Theory]
    [InlineData(-1, false)]
    [InlineData(0, false)]
    [InlineData(1, true)]
    [InlineData(1_876, true)]
    [InlineData(11_261, true)]
    [InlineData(11_262, true)]
    [InlineData(15_014, true)]
    [InlineData(22_500, true)]
    [InlineData(22_501, false)]
    public void IsTerminalChapterSentinel_MatchesHalfSecondEvidenceBasedToleranceBand(
        long ticksFromEnd, bool expected)
    {
        Assert.Equal(expected, OpticalDiscManifestGenerator.IsTerminalChapterSentinel(ticksFromEnd));
    }

    [Theory]
    [InlineData(3_753, 15_014)] // Short two-item playlist shape.
    [InlineData(44_999, 60_000)] // Final mark just under one second in.
    public void CreateBluRayChapters_PreservesFinalMarkWithLessThanOneSecondOfPrecedingContent(
        long finalMarkStart, long totalDurationTicks)
    {
        var diagnostics = new List<ManifestDiagnostic>();
        var playlist = CreateSyntheticPlaylist(
            outTime: totalDurationTicks,
            marks:
            [
                (0, MplsPlaylistMark.EntryMarkType, 0, 45_000),
                (1, MplsPlaylistMark.EntryMarkType, (uint)finalMarkStart, 45_000),
            ]);

        var chapters = OpticalDiscManifestGenerator.CreateBluRayChapters(
            playlist, diagnostics, "BDMV/PLAYLIST/00050.mpls");

        Assert.Equal(2, chapters.Count);
        Assert.Equal(Math.Round(finalMarkStart / 45_000d, 6), chapters[1].StartSeconds);
        Assert.DoesNotContain(
            diagnostics,
            item => item.Code == "ODM_BD_CHAPTER_TERMINAL_SENTINEL_EXCLUDED");
    }

    [Fact]
    public void CreateBluRayChapters_KnivesOutFiveSecondClip_ExcludesOneFrameFinalMark()
    {
        // Knives Out 1080p 00002.mpls: 227,101-tick playlist, final entry mark at 225,225
        // (1,876 ticks / one frame before end). MakeMKV reports one chapter.
        var diagnostics = new List<ManifestDiagnostic>();
        var playlist = CreateSyntheticPlaylist(
            outTime: 227_101,
            marks:
            [
                (0, MplsPlaylistMark.EntryMarkType, 0, 45_000),
                (1, MplsPlaylistMark.EntryMarkType, 225_225, 45_000),
            ]);

        var chapters = OpticalDiscManifestGenerator.CreateBluRayChapters(
            playlist, diagnostics, "BDMV/PLAYLIST/00002.mpls");

        Assert.Single(chapters);
        Assert.Contains(diagnostics, item => item.Code == "ODM_BD_CHAPTER_TERMINAL_SENTINEL_EXCLUDED");
    }

    [Fact]
    public void CreateStereoscopicView_ReturnsNullWhenNoRelationshipIsDeclared()
    {
        var diagnostics = new List<ManifestDiagnostic>();
        var playlist = CreateSyntheticPlaylist(outTime: 1_000, marks: []);

        var view = OpticalDiscManifestGenerator.CreateStereoscopicView(
            playlist, diagnostics, "BDMV/PLAYLIST/00800.mpls");

        Assert.Null(view);
        Assert.Empty(diagnostics);
    }

    [Fact]
    public void CreateStereoscopicView_MapsBaseAndDependentViewEvidence()
    {
        var diagnostics = new List<ManifestDiagnostic>();
        var playlist = CreateSyntheticPlaylist(outTime: 1_000, marks: []) with
        {
            StereoVideoRelationships = [CreateMvcStereoRelationship()],
        };

        var view = OpticalDiscManifestGenerator.CreateStereoscopicView(
            playlist, diagnostics, "BDMV/PLAYLIST/00800.mpls");

        Assert.NotNull(view);
        Assert.Equal("3d-dependent-view", view!.RelationshipType);
        Assert.Equal("00300", view.BaseClipId);
        Assert.Equal("00301", view.DependentClipId);
        Assert.True(view.IsSsVideoSubPath);
        Assert.NotNull(view.DependentStream);
        Assert.Equal(0x1012, view.DependentStream!.Pid);
        Assert.Equal(0x20, view.DependentStream.CodingTypeCode);
        Assert.Equal("MVC Video", view.DependentStream.Codec);
        Assert.Equal(6, view.DependentStream.FormatCode);
        Assert.Equal(1, view.DependentStream.RateCode);
        Assert.Empty(diagnostics);
    }

    [Fact]
    public void CreateStereoscopicView_EmitsDiagnosticWhenMultipleRelationshipsAreDeclared()
    {
        var diagnostics = new List<ManifestDiagnostic>();
        var relationship = CreateMvcStereoRelationship();
        var playlist = CreateSyntheticPlaylist(outTime: 1_000, marks: []) with
        {
            StereoVideoRelationships = [relationship, relationship],
        };

        var view = OpticalDiscManifestGenerator.CreateStereoscopicView(
            playlist, diagnostics, "BDMV/PLAYLIST/00800.mpls");

        Assert.NotNull(view);
        Assert.Contains(diagnostics, item => item.Code == "ODM_BD_3D_MULTIPLE_RELATIONSHIPS");
    }

    [Fact]
    public void AddUnsupportedExtensionDiagnostics_DoesNothingWhenNoExtensionDataIsPresent()
    {
        var diagnostics = new List<ManifestDiagnostic>();
        var playlist = CreateSyntheticPlaylist(outTime: 1_000, marks: []);

        OpticalDiscManifestGenerator.AddUnsupportedExtensionDiagnostics(
            playlist, diagnostics, "BDMV/PLAYLIST/00800.mpls");

        Assert.Empty(diagnostics);
    }

    [Fact]
    public void AddUnsupportedExtensionDiagnostics_DoesNotEmitDiagnosticsForSupportedEntries()
    {
        var diagnostics = new List<ManifestDiagnostic>();
        var playlist = CreateSyntheticPlaylist(outTime: 1_000, marks: []) with
        {
            ExtensionData = new MplsExtensionData
            {
                Length = 20,
                DataBlockStartAddress = 4,
                Entries =
                [
                    new MplsExtensionDataEntry
                    {
                        Index = 0,
                        TypeIdentifier = 2,
                        VersionIdentifier = 1,
                        RelativeStartAddress = 0,
                        Length = 10,
                        Name = "STN SS extension",
                        IsSupported = true,
                        OverlapsAnotherEntry = false,
                        IsRawDataTruncated = false,
                    },
                ],
            },
        };

        OpticalDiscManifestGenerator.AddUnsupportedExtensionDiagnostics(
            playlist, diagnostics, "BDMV/PLAYLIST/00800.mpls");

        Assert.Empty(diagnostics);
    }

    [Fact]
    public void AddUnsupportedExtensionDiagnostics_EmitsTruthfulDiagnosticForUnsupportedType3Point5()
    {
        // Parser commit 961ffd6 preserves entries it does not interpret (for example,
        // an unsupported 3.5 extension type) rather than guessing its semantics. The mapper
        // must surface this honestly and must never infer meaning for it.
        var diagnostics = new List<ManifestDiagnostic>();
        var playlist = CreateSyntheticPlaylist(outTime: 1_000, marks: []) with
        {
            ExtensionData = new MplsExtensionData
            {
                Length = 20,
                DataBlockStartAddress = 4,
                Entries =
                [
                    new MplsExtensionDataEntry
                    {
                        Index = 0,
                        TypeIdentifier = 3,
                        VersionIdentifier = 5,
                        RelativeStartAddress = 0,
                        Length = 10,
                        Name = null,
                        IsSupported = false,
                        OverlapsAnotherEntry = false,
                        RawDataHex = "0102030405",
                        IsRawDataTruncated = false,
                    },
                ],
            },
        };

        OpticalDiscManifestGenerator.AddUnsupportedExtensionDiagnostics(
            playlist, diagnostics, "BDMV/PLAYLIST/00800.mpls");

        var diagnostic = Assert.Single(diagnostics);
        Assert.Equal("ODM_BD_EXTENSION_UNSUPPORTED", diagnostic.Code);
        Assert.Equal("info", diagnostic.Severity);
        Assert.Contains("3.5", diagnostic.Message);
    }

    private static MplsStereoVideoRelationship CreateMvcStereoRelationship()
        => new()
        {
            RelationshipType = MplsStereoVideoRelationship.ThreeDimensionalDependentView,
            BasePlayItemIndex = 0,
            BaseClipId = "00300",
            BaseCodecId = "M2TS",
            BaseStcId = 0,
            DependentSubPathIndex = 0,
            DependentSubPathType = 8,
            DependentSubPlayItemIndex = 0,
            DependentClipId = "00301",
            DependentCodecId = "M2TS",
            DependentStcId = 1,
            SyncPlayItemId = 0,
            SyncPresentationTimestamp = 524_280,
            IsSsVideoSubPath = true,
            DependentViewStream = new MplsStereoVideoStream
            {
                PlayItemIndex = 0,
                VideoStreamIndex = 0,
                StreamTypeCode = 2,
                Pid = 0x1012,
                CodingTypeCode = 0x20,
                CodingType = "MVC Video",
                FormatCode = 6,
                RateCode = 1,
                RawAttributesHex = "20",
            },
        };

    private static MplsPlaylist CreateSyntheticPlaylist(
        long outTime,
        IReadOnlyList<(int Index, int MarkType, uint Time, uint Duration)> marks)
    {
        var streamTable = new MplsStreamTable
        {
            VideoStreams = [],
            AudioStreams = [],
            PresentationGraphicsStreams = [],
            InteractiveGraphicsStreams = [],
            SecondaryAudioStreams = [],
            SecondaryVideoStreams = [],
        };
        var playItem = new MplsPlayItem
        {
            Index = 0,
            ClipId = "00000",
            CodecId = "M2TS",
            ConnectionCondition = 1,
            IsMultiAngle = false,
            StcId = 0,
            InTime = 0,
            OutTime = (uint)outTime,
            RandomAccessFlag = true,
            StillMode = 0,
            Clips = [new MplsClipReference { ClipId = "00000", CodecId = "M2TS", StcId = 0 }],
            StreamTable = streamTable,
        };
        var playlistMarks = marks
            .Select(mark => new MplsPlaylistMark
            {
                Index = mark.Index,
                MarkType = mark.MarkType,
                PlayItemReference = 0,
                Time = mark.Time,
                EntryElementaryStreamPid = 0,
                Duration = mark.Duration,
            })
            .ToArray();

        return new MplsPlaylist
        {
            Identifier = "MPLS",
            Version = "0200",
            PlaylistStartAddress = 0,
            PlaylistMarkStartAddress = 0,
            ExtensionDataStartAddress = 0,
            AppInfo = new MplsAppInfo
            {
                Length = 0,
                PlaybackTypeCode = 1,
                PlaybackType = "sequential",
                RandomAccessFlag = false,
                AudioMixFlag = false,
                LosslessBypassFlag = false,
                MvcBaseViewRFlag = false,
            },
            PlayItems = [playItem],
            SubPaths = [],
            Marks = playlistMarks,
            ExtensionData = null,
            ExtensionSubPaths = [],
            StereoVideoRelationships = [],
        };
    }

    private ManifestGenerationRequest CreateRequest(
        IReadOnlyList<RecordingFile> files,
        string identifierKind,
        string identifier)
        => new()
        {
            Files = files,
            ProducerName = "test",
            ProducerVersion = "1.0.0",
            Identifiers =
            [
                new ManifestIdentifier
                {
                    Kind = identifierKind,
                    Value = identifier,
                },
            ],
        };

    private IReadOnlyList<RecordingFile> CreateBluRayFiles(string discName)
    {
        var files = new List<RecordingFile>();
        AddDirectory(files, Path.Combine(fixturesPath, "BDMV", discName), "BDMV");
        AddDirectory(files, Path.Combine(fixturesPath, "MPLS", discName), "BDMV/PLAYLIST");
        AddDirectory(files, Path.Combine(fixturesPath, "CLPI", discName), "BDMV/CLIPINF");
        files.Add(RecordingFile.Payload("BDMV/STREAM/00000.m2ts", 1_000_000));
        return files;
    }

    private static void AddDirectory(
        ICollection<RecordingFile> destination,
        string directory,
        string manifestDirectory)
    {
        foreach (var path in Directory.GetFiles(directory).OrderBy(path => path, StringComparer.Ordinal))
        {
            destination.Add(RecordingFile.FromDisk(
                $"{manifestDirectory}/{Path.GetFileName(path)}",
                path));
        }
    }

    private sealed class RecordingFile : IManifestDiscFile
    {
        private readonly string? sourcePath;
        private readonly byte[]? content;

        private RecordingFile(string path, long size, string? sourcePath, byte[]? content = null)
        {
            Path = path;
            Size = size;
            this.sourcePath = sourcePath;
            this.content = content;
        }

        public string Path { get; }

        public long Size { get; }

        public int ReadCount { get; private set; }

        public static RecordingFile FromDisk(string path, string sourcePath)
            => new(path, new FileInfo(sourcePath).Length, sourcePath);

        public static RecordingFile Payload(string path, long size)
            => new(path, size, null);

        public static RecordingFile FromBytes(string path, byte[] content)
            => new(path, content.LongLength, null, content);

        public async ValueTask<byte[]> ReadBytesAsync(
            long maxAllowedSize,
            CancellationToken cancellationToken = default)
        {
            ReadCount++;
            if (content is not null)
            {
                return content;
            }

            if (sourcePath is null)
            {
                throw new InvalidOperationException($"Payload file was read: {Path}");
            }

            if (Size > maxAllowedSize)
            {
                throw new IOException($"{Path} exceeds the read limit.");
            }

            return await File.ReadAllBytesAsync(sourcePath, cancellationToken);
        }
    }
}
