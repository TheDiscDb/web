using TheDiscDb.ImportModels;
using TheDiscDb.InputModels;
using TheDiscDb.Data.Import.Pipeline;

namespace TheDiscDb.UnitTests.DataImport;

public class DataImportItemFactoryTests
{
    [Test]
    public async Task HasInvalidChars_ValidSlug_ReturnsFalse()
    {
        var result = DataImportItemFactory.HasInvalidChars("the-matrix-1999");

        await Assert.That(result).IsFalse();
    }

    [Test]
    public async Task HasInvalidChars_WithSpaces_ReturnsTrue()
    {
        var result = DataImportItemFactory.HasInvalidChars("the matrix");

        await Assert.That(result).IsTrue();
    }

    [Test]
    public async Task HasInvalidChars_WithUnderscores_ReturnsTrue()
    {
        var result = DataImportItemFactory.HasInvalidChars("the_matrix");

        await Assert.That(result).IsTrue();
    }

    [Test]
    public async Task HasInvalidChars_WithDots_ReturnsTrue()
    {
        var result = DataImportItemFactory.HasInvalidChars("the.matrix");

        await Assert.That(result).IsTrue();
    }

    [Test]
    public async Task HasInvalidChars_PureLetters_ReturnsFalse()
    {
        var result = DataImportItemFactory.HasInvalidChars("thematrix");

        await Assert.That(result).IsFalse();
    }

    [Test]
    public async Task HasInvalidChars_PureDigits_ReturnsFalse()
    {
        var result = DataImportItemFactory.HasInvalidChars("1999");

        await Assert.That(result).IsFalse();
    }

    [Test]
    public async Task HasInvalidChars_NullOrEmpty_ReturnsFalse()
    {
        await Assert.That(DataImportItemFactory.HasInvalidChars(null!)).IsFalse();
        await Assert.That(DataImportItemFactory.HasInvalidChars("")).IsFalse();
    }

    [Test]
    public async Task HasInvalidChars_HyphenOnly_ReturnsFalse()
    {
        var result = DataImportItemFactory.HasInvalidChars("a-b-c");

        await Assert.That(result).IsFalse();
    }

    [Test]
    public async Task HasInvalidChars_SpecialChars_ReturnsTrue()
    {
        await Assert.That(DataImportItemFactory.HasInvalidChars("slug!")).IsTrue();
        await Assert.That(DataImportItemFactory.HasInvalidChars("slug@name")).IsTrue();
        await Assert.That(DataImportItemFactory.HasInvalidChars("slug#tag")).IsTrue();
        await Assert.That(DataImportItemFactory.HasInvalidChars("slug/path")).IsTrue();
    }

    [Test]
    public async Task HasInvalidChars_UnicodeLetters_ReturnsFalse()
    {
        // char.IsLetterOrDigit returns true for unicode letters
        var result = DataImportItemFactory.HasInvalidChars("café");

        await Assert.That(result).IsFalse();
    }

    [Test]
    public async Task ApplyReferenceOverrides_UsesReleaseLocalNameAndDiscId()
    {
        var disc = new Disc
        {
            Name = "Source Disc",
            GlobalDiscId = "SOURCE-ID",
        };
        var reference = new DiscReferenceFile
        {
            Name = "Referenced Release Disc",
            GlobalDiscId = "REFERENCE-ID",
        };

        DataImportItemFactory.ApplyReferenceOverrides(disc, reference);

        await Assert.That(disc.Name).IsEqualTo("Referenced Release Disc");
        await Assert.That(disc.GlobalDiscId).IsEqualTo("REFERENCE-ID");
    }

    [Test]
    public async Task ApplyReferenceOverrides_InheritsNameWhenOverrideMissing()
    {
        var disc = new Disc
        {
            Name = "Source Disc",
            GlobalDiscId = "SOURCE-ID",
        };
        var reference = new DiscReferenceFile();

        DataImportItemFactory.ApplyReferenceOverrides(disc, reference);

        await Assert.That(disc.Name).IsEqualTo("Source Disc");
        await Assert.That(disc.GlobalDiscId).IsNull();
    }
}
