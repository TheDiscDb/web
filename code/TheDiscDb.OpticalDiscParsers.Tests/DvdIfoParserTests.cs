using Xunit;
using TheDiscDb.OpticalDiscParsers.Dvd;
using TheDiscDb.OpticalDiscParsers.Input;
using TheDiscDb.OpticalDiscParsers.Diagnostics;
using System.IO;

namespace TheDiscDb.OpticalDiscParsers.Tests;

/// <summary>
/// Tests for the DVD IFO parser against real fixtures.
/// </summary>
public class DvdIfoParserTests
{
    private readonly string fixturesPath = Path.Combine(
        AppContext.BaseDirectory, 
        "fixtures"
    );

    /// <summary>
    /// Tests parsing the VMGI header from Best In Show VIDEO_TS.IFO.
    /// Validates that the parser correctly reads the disc identifier, spec version, and title counts.
    /// </summary>
    [Fact]
    public async Task ParseVmgiAsync_BestInShow_ReadsVmgiHeader()
    {
        // Arrange
        var videoTsPath = Path.Combine(fixturesPath, "Best In Show", "VIDEO_TS.IFO");
        Assert.True(File.Exists(videoTsPath), $"Fixture not found: {videoTsPath}");
        
        var bytes = File.ReadAllBytes(videoTsPath);
        var reader = new MemoryOpticalDiscReader(bytes);
        var binaryReader = new OpticalDiscBinaryReader(reader);
        var parser = new DvdIfoParser(binaryReader);

        // Act
        var result = await parser.ParseVmgiAsync();

        // Assert
        Assert.NotNull(result);
        Assert.NotNull(result.Value);
        var vmgi = result.Value;

        // Verify expected header values from Best In Show DVD
        Assert.Equal("DVDVIDEO-VMG", vmgi.Identifier);
        Assert.True(vmgi.SpecificationVersion >= 0x0100, "Spec version should be at least 0x0100");
        Assert.NotEmpty(vmgi.ProviderId);
        Assert.True(vmgi.NumberOfVolumeTitles > 0, "Should have at least one title");
        Assert.NotEmpty(vmgi.Titles);
        Assert.Equal(vmgi.NumberOfVolumeTitles, vmgi.Titles.Count);
        Assert.NotEmpty(vmgi.TitleSets);
        Assert.Equal(vmgi.NumberOfTitleSets, vmgi.TitleSets.Count);

        var firstTitle = vmgi.Titles[0];
        Assert.Equal(1, firstTitle.Number);
        Assert.Equal(1, firstTitle.TitleSetNumber);
        Assert.Equal(1, firstTitle.TitleSetTitleNumber);
        Assert.Equal(35, firstTitle.NumberOfPartsOfTitle);
        Assert.Equal(1, firstTitle.NumberOfAngles);

        // Verify no critical errors
        var criticalErrors = vmgi.Diagnostics
            .Where(d => d.Severity == DiagnosticSeverity.Error)
            .ToList();
        Assert.Empty(criticalErrors);
    }

    /// <summary>
    /// Tests parsing the VTSI header from Best In Show VTS_01_0.IFO.
    /// Validates that the parser correctly reads title set information.
    /// Note: Fixture files are stubs with minimal data, so we validate structure rather than content.
    /// </summary>
    [Fact]
    public async Task ParseVtsiAsync_BestInShowTitle1_ReadsVtsiHeader()
    {
        // Arrange
        var vts01Path = Path.Combine(fixturesPath, "Best In Show", "VTS_01_0.IFO");
        Assert.True(File.Exists(vts01Path), $"Fixture not found: {vts01Path}");
        
        var bytes = File.ReadAllBytes(vts01Path);
        var reader = new MemoryOpticalDiscReader(bytes);
        var binaryReader = new OpticalDiscBinaryReader(reader);
        var parser = new DvdIfoParser(binaryReader);

        // Act
        var result = await parser.ParseVtsiAsync(titleSetNumber: 1);

        // Assert
        Assert.NotNull(result);
        Assert.NotNull(result.Value);
        var vtsi = result.Value;

        // Verify expected header values
        Assert.Equal("DVDVIDEO-VTS", vtsi.Identifier);
        Assert.True(vtsi.SpecificationVersion >= 0x0100, "Spec version should be at least 0x0100");

        // Verify no critical errors (note: fixtures may have minimal program chain data)
        var criticalErrors = vtsi.Diagnostics
            .Where(d => d.Severity == DiagnosticSeverity.Error)
            .ToList();
        Assert.Empty(criticalErrors);
    }

    /// <summary>
    /// Tests that parsing multiple VMGI files from different discs produces consistent structure.
    /// </summary>
    [Theory]
    [InlineData("Best In Show")]
    [InlineData("Reservoir Dogs")]
    [InlineData("The Goonies")]
    public async Task ParseVmgiAsync_MultipleDvds_ConsistentStructure(string discName)
    {
        // Arrange
        var videoTsPath = Path.Combine(fixturesPath, discName, "VIDEO_TS.IFO");
        if (!File.Exists(videoTsPath))
        {
            // Skip if fixture not available
            return;
        }

        var bytes = File.ReadAllBytes(videoTsPath);
        var reader = new MemoryOpticalDiscReader(bytes);
        var binaryReader = new OpticalDiscBinaryReader(reader);
        var parser = new DvdIfoParser(binaryReader);

        // Act
        var result = await parser.ParseVmgiAsync();

        // Assert - all VMGIs should have these properties
        Assert.NotNull(result.Value);
        var vmgi = result.Value;

        Assert.NotNull(vmgi.Identifier);
        Assert.NotEmpty(vmgi.Identifier);
        Assert.NotNull(vmgi.ProviderId);
        Assert.True(vmgi.NumberOfVolumeTitles > 0);
        Assert.NotNull(vmgi.Titles);
        Assert.Equal(vmgi.NumberOfVolumeTitles, vmgi.Titles.Count);
        Assert.NotNull(vmgi.TitleSets);
        Assert.True(vmgi.TitleSets.Count <= vmgi.NumberOfTitleSets);

        // Verify logical title and title set structure.
        foreach (var title in vmgi.Titles)
        {
            Assert.True(title.Number > 0);
            Assert.True(title.NumberOfAngles > 0);
            Assert.True(title.NumberOfPartsOfTitle > 0);
            Assert.True(title.TitleSetNumber > 0);
            Assert.True(title.TitleSetTitleNumber > 0);
        }

        foreach (var titleSet in vmgi.TitleSets)
        {
            Assert.True(titleSet.Number > 0);
            Assert.True(titleSet.StartSectorAddress > 0);
        }
    }

    /// <summary>
    /// Tests that VTSI parsing returns valid stream and program chain collections.
    /// This is Phase 3.5 validation: ensure parsing doesn't crash and returns expected types.
    /// </summary>
    [Fact]
    public async Task ParseVtsiAsync_ReturnsValidStreamsAndProgramChains()
    {
        // Arrange
        var vts01Path = Path.Combine(fixturesPath, "Best In Show", "VTS_01_0.IFO");
        Assert.True(File.Exists(vts01Path), $"Fixture not found: {vts01Path}");
        
        var bytes = File.ReadAllBytes(vts01Path);
        var reader = new MemoryOpticalDiscReader(bytes);
        var binaryReader = new OpticalDiscBinaryReader(reader);
        var parser = new DvdIfoParser(binaryReader);

        // Act
        var result = await parser.ParseVtsiAsync(titleSetNumber: 1);

        // Assert
        Assert.NotNull(result);
        Assert.NotNull(result.Value);
        var vtsi = result.Value;

        // Verify stream collections are not null (even if empty for stub fixtures)
        Assert.NotNull(vtsi.VideoStreams);
        Assert.NotNull(vtsi.AudioStreams);
        Assert.NotNull(vtsi.SubtitleStreams);
        Assert.NotNull(vtsi.ProgramChains);
        Assert.NotNull(vtsi.TitlePartMaps);

        // Verify no critical errors in parsing
        var criticalErrors = vtsi.Diagnostics
            .Where(d => d.Severity == DiagnosticSeverity.Error)
            .ToList();
        Assert.Empty(criticalErrors);
    }

    /// <summary>
    /// Tests that video stream parsing extracts codec and PAL/NTSC information correctly.
    /// </summary>
    [Fact]
    public async Task ParseVtsiAsync_VideoParsing_ExtractsCodecAndStandard()
    {
        // Arrange
        var vts01Path = Path.Combine(fixturesPath, "Best In Show", "VTS_01_0.IFO");
        var bytes = File.ReadAllBytes(vts01Path);
        var reader = new MemoryOpticalDiscReader(bytes);
        var binaryReader = new OpticalDiscBinaryReader(reader);
        var parser = new DvdIfoParser(binaryReader);

        // Act
        var result = await parser.ParseVtsiAsync(titleSetNumber: 1);
        var vtsi = result.Value!;

        // Assert
        // DVD always has at most 1 video stream
        Assert.NotNull(vtsi.VideoStreams);
        Assert.True(vtsi.VideoStreams!.Count <= 1, $"Expected <= 1 video stream, got {vtsi.VideoStreams.Count}");

        if (vtsi.VideoStreams.Count > 0)
        {
            var video = vtsi.VideoStreams[0]!;
            
            // Video must be MPEG-2
            Assert.Equal("MPEG-2", video.Codec);
            
            // Index must be 0
            Assert.Equal(0, video.Index);
            
            // Frame rate must be valid (PAL=25.0 or NTSC=29.97)
            Assert.True(video.FrameRate == 25.0 || video.FrameRate == 29.97,
                $"Expected frame rate 25.0 or 29.97, got {video.FrameRate}");
            
            // Resolution must match PAL/NTSC
            if (video.IsPal)
            {
                Assert.Equal("720x576", video.Resolution);
                Assert.Equal(25.0, video.FrameRate);
            }
            else
            {
                Assert.Equal("720x480", video.Resolution);
                Assert.Equal(29.97, video.FrameRate);
            }
        }
    }

    /// <summary>
    /// Decodes VTS_V_ATR/VTS_SPST_ATR per the DVD-Video bit layout. Raw bytes and MakeMKV baselines:
    /// Fight Club VTS2 = 0x4E 0x80 (NTSC 16:9, CC field 1; MakeMKV 16:9 + CC608),
    /// Fight Club VTS3 = 0x43 0x00 (NTSC 4:3, previously misreported as PAL 720x576),
    /// Reservoir Dogs VTS3 = 0x43 0x00 (MakeMKV titles 14-16 report 4:3).
    /// </summary>
    [Theory]
    [InlineData("Fight Club", 2, "16:9", true, false)]
    [InlineData("Fight Club", 3, "4:3", false, false)]
    [InlineData("Fight Club", 5, "16:9", false, false)]
    [InlineData("Reservoir Dogs", 1, "16:9", true, false)]
    [InlineData("Reservoir Dogs", 3, "4:3", false, false)]
    [InlineData("The Goonies", 2, "4:3", true, false)]
    public async Task ParseVtsiAsync_VideoAttributes_DecodeStandardAspectAndLine21Captions(
        string disc, int titleSet, string aspectRatio, bool ccField1, bool ccField2)
    {
        var bytes = File.ReadAllBytes(Path.Combine(fixturesPath, disc, $"VTS_{titleSet:00}_0.IFO"));
        var parser = new DvdIfoParser(new OpticalDiscBinaryReader(new MemoryOpticalDiscReader(bytes)));

        var video = Assert.Single((await parser.ParseVtsiAsync(titleSet)).Value!.VideoStreams!)!;

        Assert.Equal("MPEG-2", video.Codec);
        Assert.False(video.IsPal);
        Assert.Equal(29.97, video.FrameRate);
        Assert.Equal("720x480", video.Resolution);
        Assert.Equal(aspectRatio, video.AspectRatio);
        Assert.Equal(ccField1, video.HasLine21ClosedCaptionField1);
        Assert.Equal(ccField2, video.HasLine21ClosedCaptionField2);
    }

    [Theory]
    [InlineData("Fight Club", 2)]
    [InlineData("Fight Club", 3)]
    [InlineData("The Goonies", 1)]
    [InlineData("Best In Show", 1)]
    public async Task ParseVtsiAsync_SubpictureAttributes_DecodeRleCodingModeFromHighBits(string disc, int titleSet)
    {
        var bytes = File.ReadAllBytes(Path.Combine(fixturesPath, disc, $"VTS_{titleSet:00}_0.IFO"));
        var parser = new DvdIfoParser(new OpticalDiscBinaryReader(new MemoryOpticalDiscReader(bytes)));

        var subtitles = (await parser.ParseVtsiAsync(titleSet)).Value!.SubtitleStreams!;

        Assert.NotEmpty(subtitles);
        // Language-type bits (1-0) must not leak into the coding mode (bits 7-5, 0 = 2-bit RLE).
        Assert.All(subtitles, subtitle => Assert.Equal("RLE", subtitle!.CodingMode));
    }

    /// <summary>
    /// Tests that audio stream parsing extracts codec information correctly.
    /// </summary>
    [Fact]
    public async Task ParseVtsiAsync_AudioParsing_ExtractsCodecTypes()
    {
        // Arrange
        var vts01Path = Path.Combine(fixturesPath, "Best In Show", "VTS_01_0.IFO");
        var bytes = File.ReadAllBytes(vts01Path);
        var reader = new MemoryOpticalDiscReader(bytes);
        var binaryReader = new OpticalDiscBinaryReader(reader);
        var parser = new DvdIfoParser(binaryReader);

        // Act
        var result = await parser.ParseVtsiAsync(titleSetNumber: 1);
        var vtsi = result.Value!;

        // Assert
        // DVD supports up to 8 audio streams
        Assert.NotNull(vtsi.AudioStreams);
        Assert.True(vtsi.AudioStreams!.Count <= 8, 
            $"Expected <= 8 audio streams, got {vtsi.AudioStreams.Count}");

        // Check that all audio streams have valid codecs
        var validCodecs = new[] { "AC-3", "MPEG-1 Layer 2", "MPEG-2", "LPCM", "DTS", "SDDS" };
        foreach (var audio in vtsi.AudioStreams)
        {
            Assert.Contains(audio!.Codec, validCodecs);
            Assert.True(audio.Index >= 0 && audio.Index < 8, 
                $"Invalid audio stream index: {audio.Index}");
            Assert.False(string.IsNullOrEmpty(audio.LanguageCode));
            Assert.True(audio.SamplingFrequency > 0, 
                $"Invalid sampling frequency: {audio.SamplingFrequency}");
        }
    }

    /// <summary>
    /// Tests that subtitle stream parsing extracts coding mode information correctly.
    /// </summary>
    [Fact]
    public async Task ParseVtsiAsync_SubtitleParsing_ExtractsCodingModes()
    {
        // Arrange
        var vts01Path = Path.Combine(fixturesPath, "Best In Show", "VTS_01_0.IFO");
        var bytes = File.ReadAllBytes(vts01Path);
        var reader = new MemoryOpticalDiscReader(bytes);
        var binaryReader = new OpticalDiscBinaryReader(reader);
        var parser = new DvdIfoParser(binaryReader);

        // Act
        var result = await parser.ParseVtsiAsync(titleSetNumber: 1);
        var vtsi = result.Value!;

        // Assert
        // DVD supports up to 32 subtitle streams
        Assert.NotNull(vtsi.SubtitleStreams);
        Assert.True(vtsi.SubtitleStreams!.Count <= 32, 
            $"Expected <= 32 subtitle streams, got {vtsi.SubtitleStreams.Count}");

        // Check that all subtitle streams have valid coding modes
        var validModes = new[] { "RLE", "Extended", "Other", "Unknown" };
        foreach (var subtitle in vtsi.SubtitleStreams)
        {
            Assert.Contains(subtitle!.CodingMode, validModes);
            Assert.True(subtitle.Index >= 0 && subtitle.Index < 32, 
                $"Invalid subtitle stream index: {subtitle.Index}");
            Assert.False(string.IsNullOrEmpty(subtitle.LanguageCode));
        }
    }

    /// <summary>
    /// Tests stream parsing across multiple fixture files to ensure robustness.
    /// </summary>
    [Theory]
    [InlineData("Best In Show", "VTS_01_0.IFO")]
    [InlineData("Best In Show", "VTS_02_0.IFO")]
    [InlineData("Reservoir Dogs", "VTS_01_0.IFO")]
    [InlineData("Reservoir Dogs", "VTS_02_0.IFO")]
    [InlineData("The Goonies", "VTS_01_0.IFO")]
    [InlineData("The Goonies", "VTS_02_0.IFO")]
    public async Task ParseVtsiAsync_MultipleFixtures_ParsesSuccessfully(string movie, string filename)
    {
        // Arrange
        var vtsPath = Path.Combine(fixturesPath, movie, filename);
        
        if (!File.Exists(vtsPath))
        {
            // Skip if fixture doesn't exist
            return;
        }
        
        var bytes = File.ReadAllBytes(vtsPath);
        var reader = new MemoryOpticalDiscReader(bytes);
        var binaryReader = new OpticalDiscBinaryReader(reader);
        var parser = new DvdIfoParser(binaryReader);

        // Act
        var result = await parser.ParseVtsiAsync(titleSetNumber: 1);

        // Assert
        Assert.NotNull(result);
        Assert.NotNull(result.Value);
        
        var vtsi = result.Value;
        Assert.Equal("DVDVIDEO-VTS", vtsi.Identifier);
        
        // Verify collections are valid
        Assert.NotNull(vtsi.VideoStreams);
        Assert.NotNull(vtsi.AudioStreams);
        Assert.NotNull(vtsi.SubtitleStreams);
        Assert.NotNull(vtsi.ProgramChains);
        Assert.NotNull(vtsi.TitlePartMaps);
        
        // Verify no parsing errors
        var errors = vtsi.Diagnostics
            .Where(d => d.Severity == DiagnosticSeverity.Error)
            .ToList();
        Assert.Empty(errors);
    }

    /// <summary>
    /// Tests that program chains are created even when PGC table is not readable.
    /// </summary>
    [Fact]
    public async Task ParseVtsiAsync_ProgramChainParsing_CreatesEntriesForAllChains()
    {
        // Arrange
        var vts01Path = Path.Combine(fixturesPath, "Best In Show", "VTS_01_0.IFO");
        var bytes = File.ReadAllBytes(vts01Path);
        var reader = new MemoryOpticalDiscReader(bytes);
        var binaryReader = new OpticalDiscBinaryReader(reader);
        var parser = new DvdIfoParser(binaryReader);

        // Act
        var result = await parser.ParseVtsiAsync(titleSetNumber: 1);
        var vtsi = result.Value!;

        // Assert
        // Number of program chains should match the parsed count
        Assert.NotNull(vtsi.ProgramChains);
        Assert.Equal(vtsi.NumberOfProgramChains, vtsi.ProgramChains!.Count);
        
        // Each program chain should have a unique 1-based PGC number
        for (int i = 0; i < vtsi.ProgramChains.Count; i++)
        {
            var pgc = vtsi.ProgramChains[i]!;
            Assert.Equal(i + 1, pgc.Number);
            Assert.NotNull(pgc.PlaybackTime);
            Assert.NotNull(pgc.ProgramNumbers);
            Assert.NotNull(pgc.CellIndices);
            Assert.NotNull(pgc.ProgramMap);
            Assert.NotNull(pgc.CellPlayback);
            Assert.NotNull(pgc.CellPositions);
        }
    }

    /// <summary>
    /// Tests that VMGI TT_SRPT logical-title parsing follows the title table pointer.
    /// </summary>
    [Theory]
    [InlineData("Best In Show", 23, 2)]
    [InlineData("Reservoir Dogs", 17, 4)]
    [InlineData("The Goonies", 9, 2)]
    public async Task ParseVmgiAsync_ReadsTtSrptLogicalTitles(string discName, int expectedTitleCount, int expectedTitleSetCount)
    {
        // Arrange
        var videoTsPath = Path.Combine(fixturesPath, discName, "VIDEO_TS.IFO");
        var bytes = File.ReadAllBytes(videoTsPath);
        var reader = new MemoryOpticalDiscReader(bytes);
        var binaryReader = new OpticalDiscBinaryReader(reader);
        var parser = new DvdIfoParser(binaryReader);

        // Act
        var result = await parser.ParseVmgiAsync();
        var vmgi = result.Value!;

        // Assert
        Assert.Equal(expectedTitleCount, vmgi.NumberOfVolumeTitles);
        Assert.Equal(expectedTitleCount, vmgi.Titles.Count);
        Assert.Equal(expectedTitleSetCount, vmgi.NumberOfTitleSets);
        Assert.Equal(expectedTitleSetCount, vmgi.TitleSets.Count);
        Assert.All(vmgi.Titles, title =>
        {
            Assert.True(title.TitleSetNumber >= 1 && title.TitleSetNumber <= expectedTitleSetCount);
            Assert.True(title.NumberOfPartsOfTitle >= 1);
            Assert.True(title.NumberOfAngles >= 1);
            Assert.True(title.TitleSetSector > 0);
        });
    }

    /// <summary>
    /// Tests that VTSI parsing follows VTS_PTT_SRPT and VTS_PGCIT into real chapter and PGC tables.
    /// </summary>
    [Fact]
    public async Task ParseVtsiAsync_BestInShow_ReadsPttMapsAndProgramChains()
    {
        // Arrange
        var vts01Path = Path.Combine(fixturesPath, "Best In Show", "VTS_01_0.IFO");
        var bytes = File.ReadAllBytes(vts01Path);
        var reader = new MemoryOpticalDiscReader(bytes);
        var binaryReader = new OpticalDiscBinaryReader(reader);
        var parser = new DvdIfoParser(binaryReader);

        // Act
        var result = await parser.ParseVtsiAsync(titleSetNumber: 1);
        var vtsi = result.Value!;

        // Assert
        Assert.Equal(5, vtsi.TitlePartMaps.Count);
        Assert.Equal(35, vtsi.TitlePartMaps[0].Parts.Count);
        Assert.Equal(5, vtsi.ProgramChains.Count);

        var mainPgc = vtsi.ProgramChains[0];
        Assert.Equal(1, mainPgc.Number);
        Assert.True(mainPgc.IsEntryProgramChain);
        Assert.Equal(35, mainPgc.NumberOfPrograms);
        Assert.Equal(35, mainPgc.NumberOfCells);
        Assert.Equal(35, mainPgc.ProgramMap.Count);
        Assert.Equal(8, mainPgc.AudioControls.Count);
        Assert.Equal(32, mainPgc.SubpictureControls.Count);
        Assert.All(mainPgc.AudioControls, control => Assert.True(control.StreamIndex is >= 0 and < 8));
        Assert.All(mainPgc.SubpictureControls, control => Assert.True(control.StreamIndex is >= 0 and < 32));
        Assert.Equal(35, mainPgc.CellPlayback.Count);
        Assert.Equal(35, mainPgc.CellPositions.Count);
        Assert.Equal(0, mainPgc.CellPlayback[0].StillTime);
        Assert.Equal(0, mainPgc.CellPlayback[0].CellCommandNumber);
        Assert.Equal(1, vtsi.TitlePartMaps[0].Parts[0].ProgramChainNumber);
        Assert.Equal(1, vtsi.TitlePartMaps[0].Parts[0].ProgramNumber);
        Assert.Equal(1, vtsi.TitlePartMaps[0].Parts[0].Number);
    }

    /// <summary>
    /// Tests that truncated table pointers surface diagnostics instead of placeholder success.
    /// </summary>
    [Fact]
    public async Task ParseVtsiAsync_TruncatedPttTable_ReportsDiagnostic()
    {
        // Arrange
        var vts01Path = Path.Combine(fixturesPath, "Best In Show", "VTS_01_0.IFO");
        var bytes = File.ReadAllBytes(vts01Path).Take(2050).ToArray();
        var reader = new MemoryOpticalDiscReader(bytes);
        var binaryReader = new OpticalDiscBinaryReader(reader);
        var parser = new DvdIfoParser(binaryReader);

        // Act
        var result = await parser.ParseVtsiAsync(titleSetNumber: 1);
        var vtsi = result.Value!;

        // Assert
        Assert.Empty(vtsi.TitlePartMaps);
        Assert.Contains(vtsi.Diagnostics, diagnostic => diagnostic.Code == "VD031" || diagnostic.Code == "VD099");
    }
}
