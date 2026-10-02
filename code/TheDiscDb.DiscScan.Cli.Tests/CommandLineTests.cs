namespace TheDiscDb.DiscScan.Cli.Tests;

public sealed class CommandLineTests
{
    [Theory]
    [InlineData("--help")]
    [InlineData("-h")]
    [InlineData("-?")]
    public async Task Help_PrintsGeneratedHelpWithoutRequiringDisc(string option)
    {
        using var output = new StringWriter();
        using var error = new StringWriter();

        int exitCode = await Program.RunAsync([option], output, error, TestContext.Current.CancellationToken);

        Assert.Equal(Program.Success, exitCode);
        Assert.Contains("--output", output.ToString());
        Assert.Contains("--stdout", output.ToString());
        Assert.Contains("list", output.ToString());
        Assert.Empty(error.ToString());
    }

    [Fact]
    public async Task Version_PrintsVersionWithoutRequiringDisc()
    {
        using var output = new StringWriter();
        using var error = new StringWriter();

        int exitCode = await Program.RunAsync(["--version"], output, error, TestContext.Current.CancellationToken);

        Assert.Equal(Program.Success, exitCode);
        Assert.Contains(Program.GetVersion(), output.ToString());
        Assert.Empty(error.ToString());
    }

    public static TheoryData<string[]> InvalidArguments => new()
    {
        Array.Empty<string>(),
        new[] { "--unknown" },
        new[] { "disc", "extra" },
        new[] { "disc", "--output" },
        new[] { "disc", "-o" },
        new[] { "disc", "--stdout", "-o", "manifest.json" },
        new[] { "disc", "-q", "-v" },
        new[] { "list", "-q", "-v" }
    };

    [Theory]
    [MemberData(nameof(InvalidArguments))]
    public async Task InvalidArguments_ReturnBadArgumentsAndWriteError(string[] args)
    {
        using var output = new StringWriter();
        using var error = new StringWriter();

        int exitCode = await Program.RunAsync(args, output, error, TestContext.Current.CancellationToken);

        Assert.Equal(Program.BadArguments, exitCode);
        Assert.NotEmpty(error.ToString());
    }

    [Theory]
    [InlineData("--list")]
    [InlineData("list")]
    public async Task List_DoesNotRequireDiscPath(string option)
    {
        using var output = new StringWriter();
        using var error = new StringWriter();

        int exitCode = await Program.RunAsync([option], output, error, TestContext.Current.CancellationToken);

        Assert.True(exitCode is Program.Success or Program.NoDiscFound);
        Assert.DoesNotContain("A disc path is required", error.ToString());
    }
}
