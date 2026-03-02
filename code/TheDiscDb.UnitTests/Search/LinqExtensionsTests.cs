namespace TheDiscDb.UnitTests.Search;

public class LinqExtensionsTests
{
    [Test]
    public async Task Batch_ExactMultiple_ReturnsEvenBatches()
    {
        var source = new[] { 1, 2, 3, 4, 5, 6 };
        var batches = source.Batch(3).ToList();

        await Assert.That(batches).HasCount().EqualTo(2);
        await Assert.That(batches[0].ToList()).IsEquivalentTo(new[] { 1, 2, 3 });
        await Assert.That(batches[1].ToList()).IsEquivalentTo(new[] { 4, 5, 6 });
    }

    [Test]
    public async Task Batch_NotExactMultiple_LastBatchHasRemainder()
    {
        var source = new[] { 1, 2, 3, 4, 5 };
        var batches = source.Batch(3).ToList();

        await Assert.That(batches).HasCount().EqualTo(2);
        await Assert.That(batches[0].ToList()).IsEquivalentTo(new[] { 1, 2, 3 });
        await Assert.That(batches[1].ToList()).IsEquivalentTo(new[] { 4, 5 });
    }

    [Test]
    public async Task Batch_EmptySource_ReturnsNoBatches()
    {
        var source = Array.Empty<int>();
        var batches = source.Batch(3).ToList();

        await Assert.That(batches).HasCount().EqualTo(0);
    }

    [Test]
    public async Task Batch_SingleItem_ReturnsSingleBatchWithOneItem()
    {
        var source = new[] { 42 };
        var batches = source.Batch(3).ToList();

        await Assert.That(batches).HasCount().EqualTo(1);
        await Assert.That(batches[0].ToList()).IsEquivalentTo(new[] { 42 });
    }

    [Test]
    public async Task Batch_SizeOfOne_ReturnsIndividualItems()
    {
        var source = new[] { 1, 2, 3 };
        var batches = source.Batch(1).ToList();

        await Assert.That(batches).HasCount().EqualTo(3);
        await Assert.That(batches[0].ToList()).IsEquivalentTo(new[] { 1 });
        await Assert.That(batches[1].ToList()).IsEquivalentTo(new[] { 2 });
        await Assert.That(batches[2].ToList()).IsEquivalentTo(new[] { 3 });
    }

    [Test]
    public async Task Batch_LargerThanSource_ReturnsSingleBatch()
    {
        var source = new[] { 1, 2 };
        var batches = source.Batch(10).ToList();

        await Assert.That(batches).HasCount().EqualTo(1);
        await Assert.That(batches[0].ToList()).IsEquivalentTo(new[] { 1, 2 });
    }

    [Test]
    public async Task Batch_WithStrings_Works()
    {
        var source = new[] { "a", "b", "c", "d" };
        var batches = source.Batch(2).ToList();

        await Assert.That(batches).HasCount().EqualTo(2);
        await Assert.That(batches[0].ToList()).IsEquivalentTo(new[] { "a", "b" });
        await Assert.That(batches[1].ToList()).IsEquivalentTo(new[] { "c", "d" });
    }
}
