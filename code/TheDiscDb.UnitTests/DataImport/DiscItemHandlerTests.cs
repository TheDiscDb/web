using TheDiscDb.Data.Import.Pipeline;
using TheDiscDb.InputModels;

namespace TheDiscDb.UnitTests.DataImport;

public class DiscItemHandlerTests
{
    // IsMatch never touches the title handler, so a null is fine for these cases.
    private static DiscItemHandler CreateHandler() => new(titleItemHandler: null!);

    [Test]
    public async Task IsMatch_SameContentHash_ReturnsTrue()
    {
        var handler = CreateHandler();
        var a = new Disc { Format = "UHD", ContentHash = "abc" };
        var b = new Disc { Format = "UHD", ContentHash = "abc" };

        await Assert.That(handler.IsMatch(a, b)).IsTrue();
    }

    [Test]
    public async Task IsMatch_PlaceholderOnEitherSide_ReturnsFalse()
    {
        var handler = CreateHandler();
        var placeholder = new Disc { Format = "UHD", Slug = "disc-1", IsPlaceholder = true };
        var real = new Disc { Format = "UHD", Slug = "disc-1" };

        // Placeholders are release-specific and must never dedup/share, even when slug+format match.
        await Assert.That(handler.IsMatch(placeholder, real)).IsFalse();
        await Assert.That(handler.IsMatch(real, placeholder)).IsFalse();
    }

    [Test]
    public async Task IsMatch_TwoPlaceholdersSameSlugFormat_ReturnsFalse()
    {
        var handler = CreateHandler();
        var a = new Disc { Format = "UHD", Slug = "disc-1", IsPlaceholder = true };
        var b = new Disc { Format = "UHD", Slug = "disc-1", IsPlaceholder = true };

        await Assert.That(handler.IsMatch(a, b)).IsFalse();
    }

    [Test]
    public async Task IsMatch_SameSlugAndFormat_ReturnsTrue()
    {
        var handler = CreateHandler();
        var a = new Disc { Format = "Blu-ray", Slug = "disc-1" };
        var b = new Disc { Format = "Blu-ray", Slug = "disc-1" };

        await Assert.That(handler.IsMatch(a, b)).IsTrue();
    }

    [Test]
    public async Task IsMatch_NullArgument_ReturnsFalse()
    {
        var handler = CreateHandler();
        var a = new Disc { Format = "UHD", ContentHash = "abc" };

        await Assert.That(handler.IsMatch(a, null!)).IsFalse();
        await Assert.That(handler.IsMatch(null!, a)).IsFalse();
    }
}
