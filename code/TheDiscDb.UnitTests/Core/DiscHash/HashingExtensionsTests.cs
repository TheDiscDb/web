namespace TheDiscDb.UnitTests.Core.DiscHash;

using System.Security.Cryptography;
using TheDiscDb.Core.DiscHash;

public class HashingExtensionsTests
{
    [Test]
    [Arguments(0)]
    [Arguments(1)]
    [Arguments(6)]
    [Arguments(7)]
    [Arguments(8)]
    [Arguments(9)]
    [Arguments(15)]
    [Arguments(16)]
    [Arguments(17)]
    [Arguments(128)]
    [Arguments(4096)]
    public async Task CalculateHash_PreservesLegacyContentHash(int fileCount)
    {
        var files = Enumerable.Range(0, fileCount)
            .Select(index => new FileHashInfo
            {
                Name = $"{index:00000}.m2ts",
                Size = (long)int.MaxValue + index * 1_234_567L,
            })
            .ToArray();

        await Assert.That(files.CalculateHash()).IsEqualTo(CalculateLegacyHash(files));
    }

    [Test]
    [Arguments(new long[] { }, "D41D8CD98F00B204E9800998ECF8427E")]
    [Arguments(new long[] { 0 }, "7DEA362B3FAC8E00956A4952A3D4F474")]
    [Arguments(new long[] { 1 }, "33CDECCCCEBE80329F1FDBEE7F5874CB")]
    [Arguments(new long[] { 1, 2 }, "CA57733A518406F3197F92EC197B0A0A")]
    [Arguments(new long[] { 2, 1 }, "B3FE775066BFA282320DDE52390FED36")]
    [Arguments(new long[] { 0x0102030405060708 }, "8A7334BACF760FF26B99FAD472DED851")]
    [Arguments(new long[] { 0, 1, int.MaxValue, 2147483648L, 4294967296L, long.MaxValue }, "A4EFBA1C9ADD964CD6C64EA8B055CD47")]
    [Arguments(new long[] { long.MinValue, -1, 0, long.MaxValue }, "34043747F9664A405BA4B8E1459A30DE")]
    public async Task CalculateHash_MatchesFrozenCompatibilityVectors(long[] sizes, string expected)
    {
        var files = sizes.Select(size => new FileHashInfo { Size = size });

        await Assert.That(files.CalculateHash()).IsEqualTo(expected);
    }

    [Test]
    [Arguments(12345)]
    [Arguments(98765)]
    [Arguments(314159)]
    public async Task CalculateHash_MatchesLegacyAlgorithmAcrossRandomInputs(int seed)
    {
        var random = new Random(seed);
        for (int fileCount = 0; fileCount <= 512; fileCount++)
        {
            var bytes = new byte[fileCount * sizeof(long)];
            random.NextBytes(bytes);
            var files = Enumerable.Range(0, fileCount)
                .Select(index => new FileHashInfo { Size = BitConverter.ToInt64(bytes, index * sizeof(long)) })
                .ToArray();

            await Assert.That(files.CalculateHash()).IsEqualTo(CalculateLegacyHash(files));
        }
    }

    [Test]
    public async Task CalculateHash_IgnoresNonSizeMetadata()
    {
        FileHashInfo[] files =
        [
            new() { Name = "00000.m2ts", Index = 0, Size = 123456789, CreationTime = new DateTime(2000, 1, 1) },
            new() { Name = "00001.m2ts", Index = 1, Size = 987654321, CreationTime = new DateTime(2000, 1, 2) },
        ];
        var renamedFiles = files.Select(file => file with
        {
            Name = "different-name.vob",
            Index = file.Index + 100,
            CreationTime = file.CreationTime.AddYears(10),
        });

        await Assert.That(files.CalculateHash()).IsEqualTo(renamedFiles.CalculateHash());
    }

    [Test]
    public async Task CalculateHash_PreservesInputOrder()
    {
        FileHashInfo[] files = [new() { Size = 1 }, new() { Size = 2 }];

        await Assert.That(files.CalculateHash()).IsNotEqualTo(files.Reverse().CalculateHash());
    }

    private static string CalculateLegacyHash(IEnumerable<FileHashInfo> files)
    {
        using HashAlgorithm hash = MD5.Create();
        foreach (var file in files)
        {
            byte[] fileSizeBytes = BitConverter.GetBytes(file.Size);
            hash.TransformBlock(fileSizeBytes, 0, fileSizeBytes.Length, new byte[fileSizeBytes.Length], 0);
        }

        hash.TransformFinalBlock(Array.Empty<byte>(), 0, 0);
        return BitConverter.ToString(hash.Hash!).Replace("-", "");
    }
}
