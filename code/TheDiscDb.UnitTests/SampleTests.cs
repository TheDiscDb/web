namespace TheDiscDb.UnitTests;

public class SampleTests
{
    [Test]
    public async Task SampleTest_ShouldPass()
    {
        var result = 1 + 1;
        await Assert.That(result).IsEqualTo(2);
    }
}
