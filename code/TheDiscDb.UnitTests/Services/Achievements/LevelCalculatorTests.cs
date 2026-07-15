namespace TheDiscDb.UnitTests.Services.Achievements;

using TheDiscDb.Services.Achievements;

public class LevelCalculatorTests
{
    [Test]
    [Arguments(0, LevelCalculator.Newcomer)]
    [Arguments(10, LevelCalculator.Newcomer)]
    [Arguments(25, LevelCalculator.Contributor)]
    [Arguments(74, LevelCalculator.Contributor)]
    [Arguments(75, LevelCalculator.Archivist)]
    [Arguments(199, LevelCalculator.Archivist)]
    [Arguments(200, LevelCalculator.TopContributor)]
    [Arguments(499, LevelCalculator.TopContributor)]
    [Arguments(500, LevelCalculator.Curator)]
    [Arguments(10000, LevelCalculator.Curator)]
    public async Task ForPoints_MapsToExpectedLevel(int points, string expected)
    {
        await Assert.That(LevelCalculator.ForPoints(points)).IsEqualTo(expected);
    }
}
