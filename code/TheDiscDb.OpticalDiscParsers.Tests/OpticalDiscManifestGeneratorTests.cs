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
        var files = CreateBluRayFiles("Hell on Wheels Disc 1");
        var request = CreateRequest(files, "aacs-disc-id", new string('A', 40));
        var generator = new OpticalDiscManifestGenerator();

        var first = await generator.GenerateAsync(request);
        var second = await generator.GenerateAsync(request);

        Assert.True(first.Validation.IsValid, string.Join(Environment.NewLine, first.Validation.Errors));
        Assert.Equal("blu-ray", first.Manifest.Disc.Format);
        Assert.Contains("disc.files.complete", first.Manifest.Capabilities);
        Assert.Contains("disc.titles", first.Manifest.Capabilities);
        Assert.DoesNotContain("disc.titles.complete", first.Manifest.Capabilities);
        Assert.Contains("disc.streams.declared", first.Manifest.Capabilities);
        Assert.DoesNotContain("disc.streams.payload-verified", first.Manifest.Capabilities);
        Assert.Contains(first.Manifest.Diagnostics!, item => item.Code == "ODM_BD_TITLES_PARTIAL");
        Assert.NotEmpty(first.Manifest.Disc.Titles!);
        Assert.Equal(first.Json, second.Json);
        Assert.All(
            files.Where(file => file.Path.EndsWith(".m2ts", StringComparison.OrdinalIgnoreCase)),
            file => Assert.Equal(0, file.ReadCount));
    }

    [Fact]
    public async Task GenerateAsync_BluRay_UsesBackupControlFileWhenPrimaryIsMissing()
    {
        var files = CreateBluRayFiles("Hell on Wheels Disc 1")
            .Where(file => !file.Path.Equals("BDMV/index.bdmv", StringComparison.OrdinalIgnoreCase))
            .ToList();
        var indexSource = Path.Combine(fixturesPath, "BDMV", "Hell on Wheels Disc 1", "index.bdmv");
        files.Add(RecordingFile.FromDisk("BDMV/BACKUP/index.bdmv", indexSource));
        var generator = new OpticalDiscManifestGenerator();

        var result = await generator.GenerateAsync(
            CreateRequest(files, "aacs-disc-id", new string('E', 40)));

        Assert.True(result.Validation.IsValid, string.Join(Environment.NewLine, result.Validation.Errors));
        Assert.Equal("blu-ray", result.Manifest.Disc.Format);
        Assert.Contains(result.Manifest.Disc.Files, item =>
            item.Path == "BDMV/BACKUP/index.bdmv"
            && item.Role == "backup");
        Assert.Contains(result.Manifest.Diagnostics!, item =>
            item.Code == "ODM_CONTROL_FILE_BACKUP_USED"
            && item.Path == "BDMV/BACKUP/index.bdmv");
        Assert.DoesNotContain(result.Manifest.Diagnostics!, item =>
            item.Code == "ODM_CONTROL_FILE_MISSING"
            && item.Path == "BDMV/index.bdmv");
    }

    [Fact]
    public async Task GenerateAsync_Uhd_UsesVersion0300Signal()
    {
        var files = CreateBluRayFiles("Project Hail Mary");
        var generator = new OpticalDiscManifestGenerator();

        var result = await generator.GenerateAsync(
            CreateRequest(files, "aacs-disc-id", new string('B', 40)));

        Assert.True(result.Validation.IsValid, string.Join(Environment.NewLine, result.Validation.Errors));
        Assert.Equal("uhd-blu-ray", result.Manifest.Disc.Format);
        Assert.NotEmpty(result.Manifest.Disc.Titles!);
    }

    [Fact]
    public async Task GenerateAsync_ProjectHailMaryUhd_SurfacesHevcDynamicRangeAndColorSpaceEvidence()
    {
        // Real Project Hail Mary UHD disc CLPI evidence (00589.clpi), already verified at the
        // parser layer in ClpiParserTests.ParseAsync_ProjectHailMaryFeature_ReadsHevcAndCpiEntries:
        // DynamicRangeTypeCode=1, ColorSpaceCode=2, HdrPlusFlag=false. This test proves the ODM
        // mapper surfaces that control-file-only HDR/color-space evidence into disc.clips instead
        // of silently dropping it.
        var files = CreateBluRayFiles("Project Hail Mary")
            .Concat([RecordingFile.Payload("BDMV/STREAM/00589.m2ts", 1_000_000)])
            .ToList();
        var generator = new OpticalDiscManifestGenerator();

        var result = await generator.GenerateAsync(
            CreateRequest(files, "aacs-disc-id", new string('C', 40)));

        Assert.True(result.Validation.IsValid, string.Join(Environment.NewLine, result.Validation.Errors));

        var clip = Assert.Single(result.Manifest.Disc.Clips!, item => item.ClipId == "00589");
        var hevcStream = Assert.Single(clip.Streams!, stream => stream.Pid == 0x1011);
        Assert.Equal(1, hevcStream.DynamicRangeTypeCode);
        Assert.Equal(2, hevcStream.ColorSpaceCode);
        Assert.False(hevcStream.HdrPlusFlag);

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
    public async Task GenerateAsync_AgeOfUltron3D_MapsBaseAndDependentViewRelationshipAndClips()
    {
        // Combined base + dependent-view byte total per task evidence: 43,553,961,984.
        const long CombinedBytes = 43_553_961_984;
        const long PerClipBytes = CombinedBytes / 2;

        var files = new[]
        {
            RecordingFile.FromDisk(
                "BDMV/PLAYLIST/00800.mpls",
                Path.Combine(fixturesPath, "MPLS", "Avengers Age of Ultron 3D", "00800.mpls")),
            RecordingFile.FromDisk(
                "BDMV/CLIPINF/00301.clpi",
                Path.Combine(fixturesPath, "CLPI", "Avengers Age of Ultron 3D", "00301.clpi")),
            RecordingFile.Payload("BDMV/STREAM/00300.m2ts", PerClipBytes),
            RecordingFile.Payload("BDMV/STREAM/00301.m2ts", PerClipBytes),
        };
        var generator = new OpticalDiscManifestGenerator();
        var request = CreateRequest(files, "aacs-disc-id", new string('F', 40));

        var first = await generator.GenerateAsync(request);
        var second = await generator.GenerateAsync(request);

        Assert.True(first.Validation.IsValid, string.Join(Environment.NewLine, first.Validation.Errors));
        Assert.Equal(first.Json, second.Json);
        Assert.Contains("disc.titles.stereoscopic-3d", first.Manifest.Capabilities);
        Assert.Contains("disc.clips", first.Manifest.Capabilities);

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
        Assert.Equal(PerClipBytes, baseClip.SizeBytes);
        // No CLPI fixture is available for 00300: CLPI-derived fields must be honestly
        // absent rather than inferred or guessed.
        Assert.Null(baseClip.ClipInfoPath);
        Assert.Null(baseClip.DurationSeconds);
        Assert.Null(baseClip.DurationTicks45k);
        Assert.Null(baseClip.NumberOfSourcePackets);
        Assert.Null(baseClip.TransportStreamRecordingRate);
        Assert.Null(baseClip.Streams);

        var dependentClip = Assert.Single(clips, clip => clip.ClipId == "00301");
        Assert.Equal("BDMV/STREAM/00301.m2ts", dependentClip.StreamPath);
        Assert.Equal(PerClipBytes, dependentClip.SizeBytes);
        Assert.Equal("BDMV/CLIPINF/00301.clpi", dependentClip.ClipInfoPath);
        Assert.NotNull(dependentClip.DurationTicks45k);
        Assert.True(dependentClip.DurationTicks45k > 0);
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

    /// <summary>
    /// Real Avatar (2009) 2023 Ultimate Collector's Edition 4K disc02 evidence, captured by
    /// running the generator directly against the physically mounted disc and cross-checked
    /// against the stored MakeMKV-derived <c>disc02.json</c> fixture. Every clip byte size and
    /// segment ordering value below is copied verbatim from that real disc/fixture; only the
    /// clip payload bytes themselves are synthesized (the parser must never read them).
    /// </summary>
    private static readonly IReadOnlyDictionary<string, long> AvatarUhdDisc02ClipSizes = new Dictionary<string, long>
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

    private static readonly string[] AvatarUhdDisc02Playlist00800SegmentMap =
    [
        "00062", "00107", "00071", "00108", "00073", "00109", "00075", "00125", "00069", "00110",
        "00078", "00111", "00080", "00112", "00082", "00113", "00084", "00114", "00086", "00115",
        "00088", "00116", "00090", "00117", "00092", "00118", "00094", "00119", "00096", "00120",
        "00098", "00121", "00100", "00122", "00102", "00123", "00104", "00124", "00106",
    ];

    private static readonly string[] AvatarUhdDisc02Playlist00801SegmentMap =
    [
        "00062", "00070", "00071", "00072", "00073", "00074", "00075", "00076", "00069", "00077",
        "00078", "00079", "00080", "00081", "00082", "00083", "00084", "00085", "00086", "00087",
        "00088", "00089", "00090", "00091", "00092", "00093", "00094", "00095", "00096", "00097",
        "00098", "00099", "00100", "00101", "00102", "00103", "00104", "00105", "00106",
    ];

    [Theory]
    [InlineData("00800.mpls", 86_534_971_392L)]
    [InlineData("00801.mpls", 86_535_124_992L)]
    public async Task GenerateAsync_AvatarUhdDisc02_MatchesRealDiscSegmentOrderSizeDurationAndCorrectedChapterCount(
        string playlistFileName,
        long expectedSizeBytes)
    {
        var files = new List<RecordingFile>
        {
            RecordingFile.FromDisk(
                $"BDMV/PLAYLIST/{playlistFileName}",
                Path.Combine(fixturesPath, "MPLS", "Avatar UHD Disc02", playlistFileName)),
        };
        foreach (var (clipId, size) in AvatarUhdDisc02ClipSizes)
        {
            files.Add(RecordingFile.FromDisk(
                $"BDMV/CLIPINF/{clipId}.clpi",
                Path.Combine(fixturesPath, "CLPI", "Avatar UHD Disc02", $"{clipId}.clpi")));
            files.Add(RecordingFile.Payload($"BDMV/STREAM/{clipId}.m2ts", size));
        }

        var generator = new OpticalDiscManifestGenerator();
        var request = CreateRequest(files, "aacs-disc-id", new string('D', 40));

        var first = await generator.GenerateAsync(request);
        var second = await generator.GenerateAsync(request);

        Assert.True(first.Validation.IsValid, string.Join(Environment.NewLine, first.Validation.Errors));
        Assert.Equal(first.Json, second.Json);

        var title = Assert.Single(
            first.Manifest.Disc.Titles!,
            candidate => candidate.Source.Path == $"BDMV/PLAYLIST/{playlistFileName}");

        // Exact whole-file byte sum from the real disc / stored disc02.json fixture.
        Assert.Equal(expectedSizeBytes, title.SizeBytes);
        var expectedSegmentMapForSizeCheck = playlistFileName == "00800.mpls"
            ? AvatarUhdDisc02Playlist00800SegmentMap
            : AvatarUhdDisc02Playlist00801SegmentMap;
        Assert.Equal(
            expectedSegmentMapForSizeCheck.Sum(clip => AvatarUhdDisc02ClipSizes[clip]),
            title.SizeBytes);

        // Real-disc duration evidence (9722.003467s / 437,490,156 ticks @ 45kHz).
        Assert.Equal(437_490_156L, title.DurationTicks45k);

        // Exact 39-segment ordering per the stored disc02.json SegmentMap.
        var expectedSegmentMap = playlistFileName == "00800.mpls"
            ? AvatarUhdDisc02Playlist00800SegmentMap
            : AvatarUhdDisc02Playlist00801SegmentMap;
        Assert.Equal(39, expectedSegmentMap.Length);
        Assert.Equal(expectedSegmentMap, title.Segments!.Select(segment => segment.Clip).ToArray());

        // Raw entry marks are 36; the corrected/emitted chapter count excludes the terminal
        // chapter-end sentinel, per task evidence (final mark 11,261 ticks from playlist end).
        Assert.Equal(35, title.ChapterCount);
        Assert.Equal(35, title.Chapters!.Count);

        Assert.Contains(
            first.Manifest.Diagnostics!,
            item => item.Code == "ODM_BD_CHAPTER_TERMINAL_SENTINEL_EXCLUDED"
                && item.Path == $"BDMV/PLAYLIST/{playlistFileName}"
                && item.Message.Contains("11261"));

        // Avatar's real MPLS extension type/version (3.5) is preserved as a truthful,
        // unsupported-entry diagnostic rather than guessed at in the mapper.
        Assert.Contains(
            first.Manifest.Diagnostics!,
            item => item.Code == "ODM_BD_EXTENSION_UNSUPPORTED"
                && item.Path == $"BDMV/PLAYLIST/{playlistFileName}"
                && item.Message.Contains("3.5"));

        // Never open CLPI/M2TS payloads: only file-size metadata backs disc.clips/segments.
        Assert.All(
            files.Where(file => file.Path.EndsWith(".m2ts", StringComparison.OrdinalIgnoreCase)),
            file => Assert.Equal(0, file.ReadCount));
    }

    [Fact]
    public async Task GenerateAsync_Dvd_JoinsLogicalTitlesAndChapterTiming()
    {
        var root = Path.Combine(fixturesPath, "Best In Show");
        var files = Directory.GetFiles(root, "*.IFO")
            .Select(path => RecordingFile.FromDisk(
                $"VIDEO_TS/{Path.GetFileName(path)}",
                path))
            .Append(RecordingFile.Payload("VIDEO_TS/VTS_01_1.VOB", 1_000_000))
            .ToArray();
        var generator = new OpticalDiscManifestGenerator();

        var result = await generator.GenerateAsync(
            CreateRequest(files, "dvd-disc-id", new string('C', 32)));

        Assert.True(result.Validation.IsValid, string.Join(Environment.NewLine, result.Validation.Errors));
        Assert.Equal("dvd", result.Manifest.Disc.Format);
        Assert.Contains("disc.titles.complete", result.Manifest.Capabilities);
        Assert.Contains("disc.chapters.counts", result.Manifest.Capabilities);
        Assert.Contains("disc.chapters.timing", result.Manifest.Capabilities);
        Assert.DoesNotContain(
            result.Manifest.Diagnostics ?? [],
            item => item.Code == "ODM_DVD_PARTIAL");

        var titles = Assert.IsAssignableFrom<IReadOnlyList<ManifestTitle>>(result.Manifest.Disc.Titles);
        Assert.Equal(23, titles.Count);
        var mainTitle = titles[0];
        Assert.Equal("dvd-title", mainTitle.Source.Kind);
        Assert.Equal(1, mainTitle.Source.Title);
        Assert.Equal(1, mainTitle.Source.TitleSet);
        Assert.Equal(1, mainTitle.Source.TitleSetTitle);
        Assert.Equal(35, mainTitle.ChapterCount);
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
        // MakeMKV TINFO:11 sizes from data repo Reservoir Dogs (1992)/2002-special-edition-dvd/disc01.txt.
        long[] expected =
        [
            4_774_885_376, 141_600_768, 86_566_912, 78_424_064, 29_485_056, 42_805_248,
            378_882_048, 209_395_712, 198_991_872, 351_432_704, 185_497_600, 271_239_168,
            454_447_104, 1_671_004_160, 48_924_672, 11_198_464, 37_392_384,
        ];
        var files = CreateDvdIfoFiles("Reservoir Dogs");

        var result = await new OpticalDiscManifestGenerator().GenerateAsync(
            CreateRequest(files, "dvd-disc-id", new string('E', 32)));

        Assert.True(result.Validation.IsValid, string.Join(Environment.NewLine, result.Validation.Errors));
        var titles = result.Manifest.Disc.Titles!;
        Assert.Equal(expected, titles.Select(title => title.SizeBytes ?? -1).ToArray());
        Assert.All(files.Where(file => file.Path.EndsWith(".VOB", StringComparison.OrdinalIgnoreCase)), file => Assert.Equal(0, file.ReadCount));
    }

    [Fact]
    public async Task GenerateAsync_FightClubDvd_SizesSequentialMainFeatureAndOmitsMultiPgcSizes()
    {
        var files = CreateDvdIfoFiles("Fight Club");

        var result = await new OpticalDiscManifestGenerator().GenerateAsync(
            CreateRequest(files, "dvd-disc-id", new string('F', 32)));

        Assert.True(result.Validation.IsValid, string.Join(Environment.NewLine, result.Validation.Errors));
        var titles = result.Manifest.Disc.Titles!;
        Assert.Equal(6, titles.Count);

        var main = titles[0];
        Assert.Equal(2, main.Source.TitleSet);
        Assert.Equal(37, main.ChapterCount);
        Assert.Equal(37, main.Chapters!.Count);
        Assert.Equal(8342.527528, main.DurationSeconds);
        // Exactly MakeMKV's title size and every sector of VTS_02_1..8.VOB (all 59 cells).
        Assert.Equal(7_932_198_912L, main.SizeBytes);

        // VTS_V_ATR 0x4E 0x80: NTSC 16:9 with line-21 CC field 1 (MakeMKV: 16:9 + CC608 track).
        var video = Assert.Single(main.Streams!, stream => stream.Type == "video");
        Assert.Equal("720x480", video.Resolution);
        Assert.Equal("16:9", video.AspectRatio);
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
        var root = Path.Combine(fixturesPath, "Best In Show");
        var files = Directory.GetFiles(root, "*.IFO")
            .Where(path => !path.EndsWith("VTS_02_0.IFO", StringComparison.OrdinalIgnoreCase))
            .Select(path => RecordingFile.FromDisk(
                $"VIDEO_TS/{Path.GetFileName(path)}",
                path))
            .ToArray();
        var generator = new OpticalDiscManifestGenerator();

        var result = await generator.GenerateAsync(
            CreateRequest(files, "dvd-disc-id", new string('D', 32)));

        Assert.True(result.Validation.IsValid, string.Join(Environment.NewLine, result.Validation.Errors));
        Assert.DoesNotContain("disc.titles.complete", result.Manifest.Capabilities);
        Assert.Contains(result.Manifest.Diagnostics!, item => item.Code == "ODM_DVD_PARTIAL");
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
        Assert.Equal(0, chapters[0].StartTicks45k);
        Assert.Equal(100_000, chapters[1].StartTicks45k);
    }

    [Theory]
    [InlineData(1_876)] // Knives Out 1080p 00002/00018/00042/00200 and AoU 2D 00181 evidence (one frame).
    [InlineData(11_261)] // Spartacus (UHD) and Avatar (UHD) evidence.
    [InlineData(11_262)] // Avengers: Age of Ultron 3D evidence.
    [InlineData(15_014)] // 2001: A Space Odyssey (UHD) main feature evidence.
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
        Assert.Equal(0, chapter.StartTicks45k);
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
        Assert.Equal(finalMarkStart, chapters[1].StartTicks45k);
        Assert.DoesNotContain(
            diagnostics,
            item => item.Code == "ODM_BD_CHAPTER_TERMINAL_SENTINEL_EXCLUDED");
    }

    [Fact]
    public async Task GenerateAsync_AgeOfUltron2DShortPlaylist_PreservesLegitimateSecondChapter()
    {
        var playlist = RecordingFile.FromDisk(
            "BDMV/PLAYLIST/00050.mpls",
            Path.Combine(fixturesPath, "MPLS", "Avengers Age of Ultron 2D", "00050.mpls"));
        var generator = new OpticalDiscManifestGenerator();

        var result = await generator.GenerateAsync(
            CreateRequest([playlist], "aacs-disc-id", new string('2', 40)),
            CancellationToken.None);

        Assert.True(result.Validation.IsValid);
        var title = Assert.Single(result.Manifest.Disc.Titles!);
        Assert.Equal(2, title.ChapterCount);
        Assert.Equal([0L, 3_753L], title.Chapters!.Select(chapter => chapter.StartTicks45k));
        Assert.DoesNotContain(
            result.Manifest.Diagnostics ?? [],
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
    [InlineData(3_753, 15_014)] // Age of Ultron 2D 00050-00052/00054-00057 shape.
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
        Assert.Equal(finalMarkStart, chapters[1].StartTicks45k);
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
            StereoVideoRelationships = [CreateAgeOfUltronStereoRelationship()],
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
        var relationship = CreateAgeOfUltronStereoRelationship();
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
    public void AddUnsupportedExtensionDiagnostics_EmitsTruthfulDiagnosticForAvatarExtensionType3Point5()
    {
        // Parser commit 961ffd6 preserves entries it does not interpret (for example,
        // Avatar's 3.5 extension type) rather than guessing their semantics. The mapper
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

    private static MplsStereoVideoRelationship CreateAgeOfUltronStereoRelationship()
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
            Diagnostics = [],
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
                    ComputedBy = "producer",
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

        private RecordingFile(string path, long size, string? sourcePath)
        {
            Path = path;
            Size = size;
            this.sourcePath = sourcePath;
        }

        public string Path { get; }

        public long Size { get; }

        public int ReadCount { get; private set; }

        public static RecordingFile FromDisk(string path, string sourcePath)
            => new(path, new FileInfo(sourcePath).Length, sourcePath);

        public static RecordingFile Payload(string path, long size)
            => new(path, size, null);

        public async ValueTask<byte[]> ReadBytesAsync(
            long maxAllowedSize,
            CancellationToken cancellationToken = default)
        {
            ReadCount++;
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
