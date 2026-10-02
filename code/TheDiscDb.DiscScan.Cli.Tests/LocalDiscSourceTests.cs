using System.Text;
using TheDiscDb.OpticalDiscManifest.Generation;

namespace TheDiscDb.DiscScan.Cli.Tests;

public sealed class LocalDiscSourceTests
{
    private readonly string fixturesPath = Path.Combine(AppContext.BaseDirectory, "fixtures");

    [Fact]
    public async Task LocalDiscSource_DvdFixture_GeneratesSameManifestAsDirectGenerator()
    {
        string root = CreateDvdFixtureDiscRoot("DVD-A");
        try
        {
            Assert.True(LocalDiscSource.TryOpen(root, out LocalDiscSource? source, out string error), error);

            var result = await new OpticalDiscManifestGenerator().GenerateAsync(
                new ManifestGenerationRequest
                {
                    Files = source!.Files,
                    ProducerName = "thediscdb-scan",
                    ProducerVersion = "test",
                },
                TestContext.Current.CancellationToken);

            var expected = await new OpticalDiscManifestGenerator().GenerateAsync(
                new ManifestGenerationRequest
                {
                    Files = source.Files,
                    ProducerName = "thediscdb-scan",
                    ProducerVersion = "test",
                },
                TestContext.Current.CancellationToken);

            Assert.True(result.Validation.IsValid, string.Join(Environment.NewLine, result.Validation.Errors));
            Assert.Equal("dvd", result.Manifest.Disc.Format);
            Assert.NotEmpty(result.Manifest.Disc.Titles!);
            Assert.Equal(Encoding.UTF8.GetString(expected.Json), Encoding.UTF8.GetString(result.Json));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task Cli_WritesManifestForDvdFixture()
    {
        string outputPath = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.odm.json");
        string root = CreateDvdFixtureDiscRoot("DVD-B");
        try
        {
            using var stdout = new StringWriter();
            using var stderr = new StringWriter();

            int exitCode = await Program.RunAsync(
                [root, "--output", outputPath],
                stdout,
                stderr,
                TestContext.Current.CancellationToken);

            Assert.Equal(Program.Success, exitCode);
            Assert.True(File.Exists(outputPath));
            Assert.Contains("Upload this file on the Disc Manifest tab", stderr.ToString(), StringComparison.Ordinal);
            Assert.Contains("\"format\": \"dvd\"", await File.ReadAllTextAsync(outputPath, TestContext.Current.CancellationToken), StringComparison.Ordinal);
        }
        finally
        {
            File.Delete(outputPath);
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task Cli_RejectsExistingOutputWithoutForce()
    {
        string outputPath = Path.GetTempFileName();
        string root = CreateDvdFixtureDiscRoot("DVD-C");
        try
        {
            using var stdout = new StringWriter();
            using var stderr = new StringWriter();

            int exitCode = await Program.RunAsync(
                [root, "--output", outputPath],
                stdout,
                stderr,
                TestContext.Current.CancellationToken);

            Assert.Equal(Program.BadArguments, exitCode);
            Assert.Contains("already exists", stderr.ToString(), StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            File.Delete(outputPath);
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void EnumerateCandidateRoots_Linux_IncludesExpectedMountPatterns()
    {
        var roots = LocalDiscSource.EnumerateCandidateRootsForTest(
            "linux",
            drives: [],
            directoryExists: path => path is "/media" or "/media/alex" or "/run/media" or "/run/media/alex" or "/mnt",
            enumerateDirectories: path => path switch
            {
                "/media" => ["/media/alex"],
                "/media/alex" => ["/media/alex/MOVIE"],
                "/run/media" => ["/run/media/alex"],
                "/run/media/alex" => ["/run/media/alex/SHOW"],
                "/mnt" => ["/mnt/disc"],
                _ => [],
            });

        Assert.Contains("/media/alex/MOVIE", roots);
        Assert.Contains("/run/media/alex/SHOW", roots);
        Assert.Contains("/mnt/disc", roots);
    }

    [Fact]
    public void TryOpen_AcceptsLowercaseVideoTsDirectory()
    {
        string tempRoot = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(Path.Combine(tempRoot, "video_ts"));
            File.WriteAllBytes(Path.Combine(tempRoot, "video_ts", "VIDEO_TS.IFO"), [0]);

            Assert.True(LocalDiscSource.TryOpen(tempRoot, out LocalDiscSource? source, out string error), error);
            Assert.Equal("dvd", source!.Format);
            Assert.Contains(source.Files, file => file.Path == "video_ts/VIDEO_TS.IFO");
        }
        finally
        {
            if (Directory.Exists(tempRoot))
            {
                Directory.Delete(tempRoot, recursive: true);
            }
        }
    }

    private string CreateDvdFixtureDiscRoot(string fixtureName)
    {
        string tempRoot = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        string videoTs = Path.Combine(tempRoot, "VIDEO_TS");
        Directory.CreateDirectory(videoTs);

        foreach (string sourcePath in Directory.EnumerateFiles(Path.Combine(fixturesPath, fixtureName)))
        {
            File.Copy(sourcePath, Path.Combine(videoTs, Path.GetFileName(sourcePath)));
        }

        return tempRoot;
    }
}
