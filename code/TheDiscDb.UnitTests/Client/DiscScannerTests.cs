using TheDiscDb.Client.Interop;
using TheDiscDb.Client.Pages.Contribute;
using TheDiscDb.Core.DiscHash;
using TheDiscDb.InputModels;

namespace TheDiscDb.UnitTests.Client;

public class DiscScannerTests
{
    [Test]
    public async Task NormalizeDirectoryUploadPath_RemovesSelectedRoot()
    {
        var path = DiscPath.NormalizeDirectoryUploadPath(
            @"DISC_ROOT\BDMV\STREAM\00001.m2ts");

        await Assert.That(path).IsEqualTo("BDMV/STREAM/00001.m2ts");
    }

    [Test]
    public async Task NormalizeDirectoryUploadPath_FileSystemRoot_PreservesDiscFolder()
    {
        var path = DiscPath.NormalizeDirectoryUploadPath("BDMV/STREAM/00001.m2ts");

        await Assert.That(path).IsEqualTo("BDMV/STREAM/00001.m2ts");
    }

    [Test]
    public async Task NormalizeDirectoryUploadPath_RootLevelFile_DoesNotFailSelection()
    {
        var path = DiscPath.NormalizeDirectoryUploadPath("desktop.ini");

        await Assert.That(path).IsEqualTo("desktop.ini");
    }

    [Test]
    public async Task NormalizeDirectoryUploadPath_TraversalSegment_Throws()
    {
        await Assert.That(() =>
                DiscPath.NormalizeDirectoryUploadPath("DISC_ROOT/../AACS/Unit_Key_RO.inf"))
            .Throws<InvalidDataException>();
    }

    [Test]
    public async Task ScanAsync_BluRay_UsesDirectStreamFilesAndDuplicateAacsFile()
    {
        var aacsBytes = new byte[] { 1, 2, 3, 4, 5 };
        int mediaReadCount = 0;
        long? discIdReadLimit = null;
        var files = new[]
        {
            CreateMetadataFile("BDMV/STREAM/00002.m2ts", 20_000, () => mediaReadCount++),
            CreateMetadataFile("BDMV/STREAM/00001.M2TS", 10_000, () => mediaReadCount++),
            CreateMetadataFile("BDMV/STREAM/nested/ignored.m2ts", 30_000, () => mediaReadCount++),
            CreateReadableFile(
                "AACS/DUPLICATE/Unit_Key_RO.inf",
                aacsBytes,
                maxAllowedSize => discIdReadLimit = maxAllowedSize),
        };

        var result = await DiscScanner.ScanAsync(files);

        await Assert.That(result.Error).IsNull();
        await Assert.That(result.Format).IsEqualTo(DiscFormatConstants.BluRay);
        await Assert.That(result.GlobalDiscId).IsEqualTo(AacsDiscId.Compute(aacsBytes));
        await Assert.That(result.HashFiles.Select(file => file.Name ?? string.Empty))
            .IsEquivalentTo(["00002.m2ts", "00001.M2TS"]);
        await Assert.That(mediaReadCount).IsEqualTo(0);
        await Assert.That(discIdReadLimit).IsEqualTo(DiscScanner.MaxDiscIdFileSize);
    }

    [Test]
    public async Task ScanAsync_Dvd_HashesOrderedIfoFilesAndDirectMetadata()
    {
        var videoTs = MakeVideoTsIfo(2);
        var vts01 = new byte[] { 1, 2, 3 };
        var vts02 = new byte[] { 4, 5, 6 };
        int mediaReadCount = 0;
        var files = new[]
        {
            CreateMetadataFile("VIDEO_TS/VTS_01_1.VOB", 50_000, () => mediaReadCount++),
            CreateReadableFile("VIDEO_TS/VTS_02_0.IFO", vts02),
            CreateReadableFile("VIDEO_TS/VIDEO_TS.IFO", videoTs),
            CreateReadableFile("VIDEO_TS/VTS_01_0.IFO", vts01),
            CreateMetadataFile("VIDEO_TS/nested/ignored.vob", 99_000, () => mediaReadCount++),
        };

        var result = await DiscScanner.ScanAsync(files);

        await Assert.That(result.Error).IsNull();
        await Assert.That(result.Format).IsEqualTo(DiscFormatConstants.Dvd);
        await Assert.That(result.GlobalDiscId)
            .IsEqualTo(DvdDiscId.Compute(videoTs, new byte[]?[] { vts01, vts02 }));
        await Assert.That(result.HashFiles.Count).IsEqualTo(4);
        await Assert.That(result.HashFiles.Any(file => file.Name == "ignored.vob")).IsFalse();
        await Assert.That(mediaReadCount).IsEqualTo(0);
    }

    [Test]
    public async Task ScanAsync_WrongRoot_ReturnsActionableError()
    {
        var result = await DiscScanner.ScanAsync(
            [CreateMetadataFile("OTHER/file.bin", 1)]);

        await Assert.That(result.HashFiles).IsEmpty();
        await Assert.That(result.Error).Contains("disc root");
    }

    private static DiscScanFile CreateMetadataFile(
        string path,
        long size,
        Action? onRead = null)
        => new(
            path,
            GetName(path),
            size,
            DateTime.UnixEpoch,
            (_, _) =>
            {
                onRead?.Invoke();
                throw new InvalidOperationException("Metadata-only files must not be read.");
            });

    private static DiscScanFile CreateReadableFile(
        string path,
        byte[] bytes,
        Action<long>? onRead = null)
        => new(
            path,
            GetName(path),
            bytes.Length,
            DateTime.UnixEpoch,
            (maxAllowedSize, _) =>
            {
                onRead?.Invoke(maxAllowedSize);
                return ValueTask.FromResult(bytes);
            });

    private static string GetName(string path)
        => path.Replace('\\', '/').Split('/')[^1];

    private static byte[] MakeVideoTsIfo(int titleSetCount)
    {
        var bytes = Enumerable.Repeat((byte)0xAB, 0x100).ToArray();
        bytes[0x3E] = (byte)((titleSetCount >> 8) & 0xFF);
        bytes[0x3F] = (byte)(titleSetCount & 0xFF);
        return bytes;
    }
}
