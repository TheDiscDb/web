using TheDiscDb.Naming;

namespace TheDiscDb.UnitTests.Naming;

public class FileNameSanitizerTests
{
    [Test]
    public async Task Sanitize_ColonReplacedWithSpaceHyphenSpace()
    {
        var result = FileNameSanitizer.Sanitize("Director's Cut: Extended");

        await Assert.That(result).IsEqualTo("Director's Cut - Extended");
    }

    [Test]
    public async Task Sanitize_BackslashRemoved()
    {
        var result = FileNameSanitizer.Sanitize(@"path\to\file");

        await Assert.That(result).IsEqualTo("pathtofile");
    }

    [Test]
    public async Task Sanitize_ForwardSlashRemoved()
    {
        var result = FileNameSanitizer.Sanitize("path/to/file");

        await Assert.That(result).IsEqualTo("pathtofile");
    }

    [Test]
    public async Task Sanitize_AsteriskRemoved()
    {
        var result = FileNameSanitizer.Sanitize("star*file");

        await Assert.That(result).IsEqualTo("starfile");
    }

    [Test]
    public async Task Sanitize_QuestionMarkRemoved()
    {
        var result = FileNameSanitizer.Sanitize("what?");

        await Assert.That(result).IsEqualTo("what");
    }

    [Test]
    public async Task Sanitize_DoubleQuoteRemoved()
    {
        var result = FileNameSanitizer.Sanitize("say \"hello\"");

        await Assert.That(result).IsEqualTo("say hello");
    }

    [Test]
    public async Task Sanitize_AngleBracketsRemoved()
    {
        var result = FileNameSanitizer.Sanitize("<tag>");

        await Assert.That(result).IsEqualTo("tag");
    }

    [Test]
    public async Task Sanitize_PipeRemoved()
    {
        var result = FileNameSanitizer.Sanitize("a|b");

        await Assert.That(result).IsEqualTo("ab");
    }

    [Test]
    public async Task Sanitize_CleanStringUnchanged()
    {
        var result = FileNameSanitizer.Sanitize("The Matrix (1999) [2160p].mkv");

        await Assert.That(result).IsEqualTo("The Matrix (1999) [2160p].mkv");
    }

    [Test]
    public async Task Sanitize_MultipleIllegalChars_AllHandled()
    {
        var result = FileNameSanitizer.Sanitize("a:b*c?d\"e<f>g|h");

        await Assert.That(result).IsEqualTo("a - bcdefgh");
    }

    [Test]
    public async Task Sanitize_EmptyString_ReturnsEmpty()
    {
        var result = FileNameSanitizer.Sanitize("");

        await Assert.That(result).IsEqualTo("");
    }

    [Test]
    public async Task Sanitize_Null_ReturnsEmpty()
    {
        var result = FileNameSanitizer.Sanitize(null!);

        await Assert.That(result).IsEqualTo("");
    }

    [Test]
    public async Task Sanitize_NullChar_Removed()
    {
        var result = FileNameSanitizer.Sanitize("a\0b");

        await Assert.That(result).IsEqualTo("ab");
    }

    [Test]
    public async Task Sanitize_ControlChars_Removed()
    {
        var result = FileNameSanitizer.Sanitize("a\tb\nc\rd");

        await Assert.That(result).IsEqualTo("abcd");
    }

    [Test]
    public async Task Sanitize_MultipleColons_EachReplaced()
    {
        var result = FileNameSanitizer.Sanitize("a:b:c");

        await Assert.That(result).IsEqualTo("a - b - c");
    }
}
