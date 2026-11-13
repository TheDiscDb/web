namespace TheDiscDb.UnitTests.Core.DiscHash;

using TheDiscDb.InputModels;

public class DiscFormatConstantsTests
{
    [Test]
    [Arguments("dvd", "DVD")]
    [Arguments("blu-ray", "Blu-ray")]
    [Arguments("uhd-blu-ray", "4K")]
    public async Task FromManifestFormat_MapsDetectedFormat(string manifestFormat, string expected)
    {
        await Assert.That(DiscFormatConstants.FromManifestFormat(manifestFormat)).IsEqualTo(expected);
    }

    [Test]
    public async Task FromManifestFormat_RejectsUnknownFormat()
    {
        await Assert.That(() => DiscFormatConstants.FromManifestFormat("unknown"))
            .Throws<ArgumentException>();
    }
}
