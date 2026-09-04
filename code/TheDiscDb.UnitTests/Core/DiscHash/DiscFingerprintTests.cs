namespace TheDiscDb.UnitTests.Core.DiscHash;

using TheDiscDb.Core.DiscHash;

public class DiscFingerprintTests
{
    [Test]
    public async Task Fingerprint_PortableConformanceFixtures()
    {
        // Portable fixtures from https://github.com/shitwolfymakes/matrix256/blob/main/CONFORMANCE_FIXTURES.md.
        var fixtures = new[]
        {
            new Fixture("empty directory", _ => { }, "e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855"),
            new Fixture("single zero-byte file", root => WriteFile(root, "a"), "576ada568edb673473287643d06ca9b763d81b712a080388fbf445bf580dab3d"),
            new Fixture("single small ASCII file", root => WriteFile(root, "hello.txt", "hello\n"u8.ToArray()), "00c8e12fff1075e74071d424a34ec9e89e2ffc96c5c4ec6a5bf7a3b5941b3324"),
            new Fixture("two files at root", root =>
            {
                WriteFile(root, "b");
                WriteFile(root, "a");
            }, "a7cde029efe3b62bb536d2eead4b0900409eea281230c0e1146dd0db645a2042"),
            new Fixture("slash vs dash sort edge case", root =>
            {
                WriteFile(root, "a/b");
                WriteFile(root, "a-b");
            }, "82d1301cbc45799e538f19a52840b9ff5a9ca797d80c5e52b4d98c4750d2b5e3"),
            new Fixture("nested directories", root => WriteFile(root, "dir1/dir2/file.txt"), "8f2c64be52e682809a97f2e370a2638c10e3c3f9071eaa0bda3f7fc4c6c6eccb"),
            new Fixture("sibling directories sort by full path", root =>
            {
                WriteFile(root, "b/a");
                WriteFile(root, "a/z");
            }, "ab44545fa7095c239cd8e9fa36eff237b1cc8e32c5126e98591b24250aa11871"),
            new Fixture("only an empty subdirectory", root => Directory.CreateDirectory(Path.Combine(root, "empty")), "e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855"),
            new Fixture("file plus an empty subdirectory", root =>
            {
                Directory.CreateDirectory(Path.Combine(root, "empty"));
                WriteFile(root, "hello.txt", "hello\n"u8.ToArray());
            }, "00c8e12fff1075e74071d424a34ec9e89e2ffc96c5c4ec6a5bf7a3b5941b3324"),
            new Fixture("Latin diacritics, NFC source", root => WriteFile(root, "caf\u00E9.txt"), "afd2f606ae4f4e4d644cbb28ab2f1c5d46d6f98130304efd9941db17d6a91dcd"),
            new Fixture("Latin diacritics, NFD source", root => WriteFile(root, "cafe\u0301.txt"), "afd2f606ae4f4e4d644cbb28ab2f1c5d46d6f98130304efd9941db17d6a91dcd"),
            new Fixture("Cyrillic filename", root => WriteFile(root, "\u043F\u0440\u0438\u0432\u0435\u0442.txt"), "c044182349eea94dff66a1ce2764e6f809cbf8893b2071d5906203b41fea21c0"),
            new Fixture("Han filename", root => WriteFile(root, "\u4F60\u597D.txt"), "339e0893d9d4aa8df81e9e7d671983f7befa124bd86416dc69697c32d8112787"),
            new Fixture("Arabic filename", root => WriteFile(root, "\u0645\u0631\u062D\u0628\u0627.txt"), "9ec64191ddf011278744183c8830b3b7e7c6f35fbff37c66122f0ae0e7add033"),
            new Fixture("emoji filename", root => WriteFile(root, "\U0001F3B5.txt"), "7c547ce5b89040b67d9cbf5c2ec5556090fdcfa8f3120b48a856c054769b7816"),
            new Fixture("multi-script directory", root =>
            {
                WriteFile(root, "\U0001F3B5.txt");
                WriteFile(root, "\u4F60\u597D.txt");
                WriteFile(root, "caf\u00E9.txt");
                WriteFile(root, "ascii.txt");
            }, "b7ce4f0d4e8cde3698b11edc79c49639b3f04cf88e128b0f1c3f0951843f7966"),
            new Fixture("size boundaries", root =>
            {
                WriteFile(root, "size_0000000", 0);
                WriteFile(root, "size_0000001", 1);
                WriteFile(root, "size_0000255", 255);
                WriteFile(root, "size_0000256", 256);
                WriteFile(root, "size_0065535", 65_535);
                WriteFile(root, "size_0065536", 65_536);
                WriteFile(root, "size_1000000", 1_000_000);
            }, "ac2ee75612a4d578fe365711b2f8aef71e40b2f8c2abf212fa26308d857160e6"),
            new Fixture("many small files", root =>
            {
                for (int index = 99; index >= 0; index--)
                {
                    WriteFile(root, $"f{index:D3}");
                }
            }, "a164865515f0f66b25cc4aff36e558a602d3db6caf62d41d1e830f9283b3dc8f"),
            new Fixture("deeply nested file", root => WriteFile(root, "a/b/c/d/e/f/g/h/i/j/file.txt"), "35997ed41f132aad8afc1e08a577090dff4aaa7bb23ffe5f874e879fbc38475f"),
            new Fixture("long filename", root => WriteFile(root, new string('a', 200)), "31013f1f14b4c55273b923a96047c43e157423625160c53dad1f7971de44db58"),
            new Fixture("prefix and trailing-character sort", root =>
            {
                WriteFile(root, "foobar");
                WriteFile(root, "foo.txt");
                WriteFile(root, "foo");
            }, "599b5d5fd9d52740c6b40f134b260b52de60bed70ee60aa0536ee8474fc65bcc"),
            new Fixture("content irrelevance", root => WriteFile(root, "hello.txt", "world!"u8.ToArray()), "00c8e12fff1075e74071d424a34ec9e89e2ffc96c5c4ec6a5bf7a3b5941b3324"),
        };

        foreach (var fixture in fixtures)
        {
            string root = Path.Combine(Path.GetTempPath(), $"disc-fingerprint-{Guid.NewGuid():N}");
            Directory.CreateDirectory(root);

            try
            {
                fixture.Create(root);
                string actual = DiscFingerprint.Calculate(root);
                await Assert.That((fixture.Name, actual)).IsEqualTo((fixture.Name, fixture.Expected));
            }
            finally
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    [Test]
    public async Task Fingerprint_DoesNotFollowDirectoryLinks()
    {
        string parent = Path.Combine(Path.GetTempPath(), $"disc-fingerprint-{Guid.NewGuid():N}");
        string root = Path.Combine(parent, "root");
        string target = Path.Combine(parent, "target");
        Directory.CreateDirectory(root);
        Directory.CreateDirectory(target);
        WriteFile(target, "outside.txt", [1]);

        try
        {
            Directory.CreateSymbolicLink(Path.Combine(root, "link"), target);

            string actual = DiscFingerprint.Calculate(root);

            await Assert.That(actual).IsEqualTo("e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855");
        }
        finally
        {
            Directory.Delete(parent, recursive: true);
        }
    }

    [Test]
    public async Task Fingerprint_MetadataEntries_MatchesFilesystemFingerprint()
    {
        var files = new[]
        {
            new DiscFingerprintFile("BDMV\\STREAM\\00001.m2ts", 1234),
            new DiscFingerprintFile("cafe\u0301.txt", 0),
        };

        var actual = DiscFingerprint.Calculate(files);

        await Assert.That(actual).IsEqualTo(
            DiscFingerprint.Calculate(
            [
                new DiscFingerprintFile("café.txt", 0),
                new DiscFingerprintFile("BDMV/STREAM/00001.m2ts", 1234),
            ]));
    }

    [Test]
    [Arguments("/absolute/file", 1)]
    [Arguments("../outside", 1)]
    [Arguments("folder//file", 1)]
    [Arguments("folder/./file", 1)]
    [Arguments("file", -1)]
    public async Task Fingerprint_InvalidMetadataEntry_Throws(string path, long size)
    {
        await Assert.That(() => DiscFingerprint.Calculate([new DiscFingerprintFile(path, size)]))
            .Throws<ArgumentException>();
    }

    [Test]
    public async Task Fingerprint_DuplicateNormalizedMetadataPaths_Throws()
    {
        await Assert.That(() => DiscFingerprint.Calculate(
            [
                new DiscFingerprintFile("café.txt", 1),
                new DiscFingerprintFile("cafe\u0301.txt", 1),
            ]))
            .Throws<ArgumentException>();
    }

    private static void WriteFile(string root, string relativePath, byte[]? content = null)
    {
        string path = Path.Combine(root, relativePath.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllBytes(path, content ?? []);
    }

    private static void WriteFile(string root, string relativePath, long size)
    {
        string path = Path.Combine(root, relativePath.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);

        using var file = File.Create(path);
        file.SetLength(size);
    }

    private sealed record Fixture(string Name, Action<string> Create, string Expected);
}
