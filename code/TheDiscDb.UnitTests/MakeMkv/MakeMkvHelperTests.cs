namespace TheDiscDb.UnitTests.MakeMkvTools;

using System.Text;
using Fantastic.FileSystem;
using Microsoft.Extensions.Options;
using TheDiscDb.Tools.MakeMkv;

public class MakeMkvHelperTests
{
    [Test]
    public async Task CleanLogs_InvalidUtf8_ReplacesInvalidBytes()
    {
        string path = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(),
            $"makemkv-invalid-utf8-{Guid.NewGuid():N}.txt");
        byte[] prefix = Encoding.UTF8.GetBytes("CINFO:2,0,\"DISC ");
        byte[] suffix = Encoding.UTF8.GetBytes(" TITLE\"\r\n");
        byte[] content = [.. prefix, 0xFF, .. suffix];
        await File.WriteAllBytesAsync(path, content);

        try
        {
            var helper = new MakeMkvHelper(
                Options.Create(new MakeMkvOptions { Path = "unused" }),
                new PhysicalFileSystem());

            await helper.CleanLogs(0, path);

            string result = new UTF8Encoding(
                encoderShouldEmitUTF8Identifier: false,
                throwOnInvalidBytes: true).GetString(await File.ReadAllBytesAsync(path));
            await Assert.That(result).Contains("DISC \uFFFD TITLE");
        }
        finally
        {
            File.Delete(path);
        }
    }
}
