using TheDiscDb.Client.Pages.Contribute;

namespace TheDiscDb.UnitTests.Client;

public class ContributionDiscTitleMatcherTests
{
    [Test]
    public async Task FindMatch_DuplicateMetadata_UsesSource()
    {
        var first = new DiscTitle("00001.mpls", "1", 12, "20 GB");
        var second = new DiscTitle("00002.mpls", "1", 12, "20 GB");
        var titles = new[] { first, second };
        var matchedTitles = new HashSet<DiscTitle>();

        var match = FindMatch(titles, matchedTitles, "00002.mpls", "1", 12, "20 GB");

        await Assert.That(match).IsSameReferenceAs(second);
    }

    [Test]
    public async Task FindMatch_SourceAlreadyMatched_DoesNotGuess()
    {
        var first = new DiscTitle("00001.mpls", "1", 12, "20 GB");
        var second = new DiscTitle("00002.mpls", "1", 12, "20 GB");
        var titles = new[] { first, second };
        var matchedTitles = new HashSet<DiscTitle> { first };

        var match = FindMatch(titles, matchedTitles, "00001.mpls", "1", 12, "20 GB");

        await Assert.That(match).IsNull();
    }

    [Test]
    public async Task FindMatch_MissingSource_UsesUniqueMetadata()
    {
        var expected = new DiscTitle("00001.mpls", "1", 12, "20 GB");
        var titles = new[]
        {
            expected,
            new DiscTitle("00002.mpls", "2", 8, "10 GB")
        };

        var match = FindMatch(titles, new HashSet<DiscTitle>(), null, "1", 12, "20 GB");

        await Assert.That(match).IsSameReferenceAs(expected);
    }

    [Test]
    public async Task FindMatch_MissingSourceAndDuplicateMetadata_DoesNotGuess()
    {
        var titles = new[]
        {
            new DiscTitle("00001.mpls", "1", 12, "20 GB"),
            new DiscTitle("00002.mpls", "1", 12, "20 GB")
        };

        var match = FindMatch(titles, new HashSet<DiscTitle>(), null, "1", 12, "20 GB");

        await Assert.That(match).IsNull();
    }

    private static DiscTitle? FindMatch(
        IEnumerable<DiscTitle> titles,
        ISet<DiscTitle> matchedTitles,
        string? source,
        string? segmentMap,
        int chapterCount,
        string? size)
    {
        return ContributionDiscTitleMatcher.FindMatch(
            titles,
            matchedTitles,
            source,
            segmentMap,
            chapterCount,
            size,
            title => title.Source,
            title => title.SegmentMap,
            title => title.ChapterCount,
            title => title.Size);
    }

    private sealed record DiscTitle(string Source, string SegmentMap, int ChapterCount, string Size);
}
