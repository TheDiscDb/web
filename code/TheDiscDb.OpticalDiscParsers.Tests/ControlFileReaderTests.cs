using TheDiscDb.OpticalDiscManifest.Generation;
using TheDiscDb.OpticalDiscManifest.Models;
using TheDiscDb.OpticalDiscParsers.Diagnostics;
using TheDiscDb.OpticalDiscParsers.Models;

namespace TheDiscDb.OpticalDiscParsers.Tests;

public sealed class ControlFileReaderTests
{
    [Theory]
    [InlineData("complete", "complete", "primary", "complete", false, 1)]
    [InlineData("complete", "failed", "primary", "complete", false, 1)]
    [InlineData("partial", "complete", "backup", "complete", true, 2)]
    [InlineData("partial", "partial", "backup", "partial", true, 2)]
    [InlineData("partial", "failed", "primary", "partial", false, 2)]
    [InlineData("partial", "unreadable", "primary", "partial", false, 1)]
    [InlineData("partial", "missing", "primary", "partial", false, 1)]
    [InlineData("failed", "complete", "backup", "complete", true, 2)]
    [InlineData("failed", "partial", "backup", "partial", true, 2)]
    [InlineData("failed", "failed", "primary", null, false, 2)]
    [InlineData("unreadable", "complete", "backup", "complete", true, 1)]
    [InlineData("missing", "complete", "backup", "complete", true, 1)]
    [InlineData("missing", "failed", "backup", null, false, 1)]
    [InlineData("missing", "missing", null, null, false, 0)]
    public async Task ParseControlWithBackupAsync_SelectsUsableEvidenceAndPreservesDiagnostics(
        string primaryState,
        string backupState,
        string? expectedPath,
        string? expectedValue,
        bool backupUsed,
        int expectedReads)
    {
        var files = new List<IManifestDiscFile>();
        if (primaryState != "missing")
        {
            files.Add(new ControlFile("primary", primaryState));
        }

        if (backupState != "missing")
        {
            files.Add(new ControlFile("backup", backupState));
        }

        var diagnostics = new List<ManifestDiagnostic>();
        var catalog = DiscFileCatalog.NormalizeFiles(files, diagnostics);
        var metrics = new ReadMetrics();
        var result = await ControlFileReader.ParseControlWithBackupAsync(
            catalog, "primary", "backup", diagnostics, metrics,
            TestContext.Current.CancellationToken, ParseAsync);

        Assert.Equal(expectedPath, result?.Path);
        Assert.Equal(expectedValue, result?.Value?.State);
        Assert.Equal(expectedReads, metrics.FilesRead);
        Assert.Equal(expectedReads, metrics.BytesRead);
        Assert.Equal(backupUsed, diagnostics.Any(item => item.Code == "ODM_CONTROL_FILE_BACKUP_USED"));
        if (backupUsed)
        {
            var used = Assert.Single(diagnostics, item => item.Code == "ODM_CONTROL_FILE_BACKUP_USED");
            Assert.Equal("backup", used.Path);
        }

        foreach (var file in files.Cast<ControlFile>().Where(file => file.ReadCount > 0))
        {
            if (file.State is "partial" or "failed" or "unreadable")
            {
                Assert.Contains(diagnostics, item =>
                    item.Path == file.Path
                    && item.Code == (file.State == "unreadable" ? "ODM_CONTROL_FILE_READ" : "TEST_PARSE_ERROR"));
            }
        }
    }

    [Fact]
    public async Task ParseControlWithBackupAsync_PropagatesCancellationWithoutTryingBackup()
    {
        var primary = new ControlFile("primary", "complete");
        var backup = new ControlFile("backup", "complete");
        var diagnostics = new List<ManifestDiagnostic>();
        var catalog = DiscFileCatalog.NormalizeFiles([primary, backup], diagnostics);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            ControlFileReader.ParseControlWithBackupAsync(
                catalog, "primary", "backup", diagnostics, new ReadMetrics(),
                cancellation.Token, ParseAsync));

        Assert.Equal(0, backup.ReadCount);
        Assert.Empty(diagnostics);
    }

    private static Task<ParserResult<ControlEvidence>> ParseAsync(byte[] bytes)
    {
        string state = bytes[0] switch
        {
            0 => "failed",
            1 => "partial",
            _ => "complete",
        };
        ControlEvidence? value = state == "failed" ? null : new ControlEvidence(state);
        IReadOnlyList<ParserDiagnostic> diagnostics = state == "complete"
            ? []
            : [new ParserDiagnostic(0, DiagnosticSeverity.Error, "TEST_PARSE_ERROR", state)];
        return Task.FromResult(new ParserResult<ControlEvidence>(value, diagnostics));
    }

    private sealed record ControlEvidence(string State);

    private sealed class ControlFile(string path, string state) : IManifestDiscFile
    {
        public string Path { get; } = path;

        public string State { get; } = state;

        public long Size => 1;

        public int ReadCount { get; private set; }

        public ValueTask<byte[]> ReadBytesAsync(long maxAllowedSize, CancellationToken cancellationToken = default)
        {
            ReadCount++;
            cancellationToken.ThrowIfCancellationRequested();
            if (State == "unreadable")
            {
                throw new IOException("Cannot read control metadata.");
            }

            byte marker = State switch
            {
                "failed" => 0,
                "partial" => 1,
                _ => 2,
            };
            return ValueTask.FromResult<byte[]>([marker]);
        }
    }
}
