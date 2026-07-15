namespace TheDiscDb.UnitTests.Services.Achievements;

using TheDiscDb.Services.Achievements;

public class ContributorStatsBuilderTests
{
    [Test]
    public async Task MaxConsecutiveRun_Empty_IsZero()
    {
        await Assert.That(ContributorStatsBuilder.MaxConsecutiveRun(System.Array.Empty<int>())).IsEqualTo(0);
    }

    [Test]
    public async Task MaxConsecutiveRun_FindsLongestRun()
    {
        // months: 1,2,3 then gap then 10,11 -> longest run is 3.
        var months = new[] { 1, 2, 3, 10, 11 };
        await Assert.That(ContributorStatsBuilder.MaxConsecutiveRun(months)).IsEqualTo(3);
    }

    [Test]
    public async Task MaxConsecutiveRun_SingleValue_IsOne()
    {
        await Assert.That(ContributorStatsBuilder.MaxConsecutiveRun(new[] { 42 })).IsEqualTo(1);
    }

    [Test]
    public async Task HasGap_DetectsGapAtOrAboveThreshold()
    {
        // gap between 3 and 10 is 7 >= 6.
        await Assert.That(ContributorStatsBuilder.HasGap(new[] { 1, 2, 3, 10 }, 6)).IsTrue();
    }

    [Test]
    public async Task HasGap_NoGapWhenContiguous()
    {
        await Assert.That(ContributorStatsBuilder.HasGap(new[] { 1, 2, 3, 4, 5 }, 6)).IsFalse();
    }
}
