using TheDiscDb.Client.Pages.Contribute;

namespace TheDiscDb.UnitTests.Client;

public class ContributionDiscTitleMatcherTests
{
    [Test]
    public async Task FindMatch_DuplicateMetadata_UsesSource()
    {
        var first = new DiscTitle(1, "00001.mpls", "1", 12, "20 GB");
        var second = new DiscTitle(2, "00002.mpls", "1", 12, "20 GB");
        var titles = new[] { first, second };
        var matchedTitles = new HashSet<DiscTitle>();

        var match = FindMatch(titles, matchedTitles, "00002.mpls", "1", 12, "20 GB");

        await Assert.That(match).IsSameReferenceAs(second);
    }

    [Test]
    public async Task FindMatch_SourceAlreadyMatched_DoesNotGuess()
    {
        var first = new DiscTitle(1, "00001.mpls", "1", 12, "20 GB");
        var second = new DiscTitle(2, "00002.mpls", "1", 12, "20 GB");
        var titles = new[] { first, second };
        var matchedTitles = new HashSet<DiscTitle> { first };

        var match = FindMatch(titles, matchedTitles, "00001.mpls", "1", 12, "20 GB");

        await Assert.That(match).IsNull();
    }

    [Test]
    public async Task FindMatch_MissingSource_UsesUniqueMetadata()
    {
        var expected = new DiscTitle(1, "00001.mpls", "1", 12, "20 GB");
        var titles = new[]
        {
            expected,
            new DiscTitle(2, "00002.mpls", "2", 8, "10 GB")
        };

        var match = FindMatch(titles, new HashSet<DiscTitle>(), null, "1", 12, "20 GB");

        await Assert.That(match).IsSameReferenceAs(expected);
    }

    [Test]
    public async Task FindMatch_MissingSourceAndDuplicateMetadata_DoesNotGuess()
    {
        var titles = new[]
        {
            new DiscTitle(1, "00001.mpls", "1", 12, "20 GB"),
            new DiscTitle(2, "00002.mpls", "1", 12, "20 GB")
        };

        var match = FindMatch(titles, new HashSet<DiscTitle>(), null, "1", 12, "20 GB");

        await Assert.That(match).IsNull();
    }

    [Test]
    public async Task FindMatch_DuplicateExactMatches_ConsumesFirstAvailable()
    {
        // Two titles share an identical physical signature (same source and metadata) but are distinct
        // titles on the disc (distinct index). Multiple DB items pointing at that signature must each keep a
        // match so their DatabaseId survives reload; dropping the match is what caused duplicate rows on the
        // next save.
        var first = new DiscTitle(1, "00800.mpls", "1", 12, "20 GB");
        var second = new DiscTitle(2, "00800.mpls", "1", 12, "20 GB");
        var titles = new[] { first, second };
        var matchedTitles = new HashSet<DiscTitle>();

        var firstMatch = FindMatch(titles, matchedTitles, "00800.mpls", "1", 12, "20 GB");
        matchedTitles.Add(firstMatch!);
        var secondMatch = FindMatch(titles, matchedTitles, "00800.mpls", "1", 12, "20 GB");

        await Assert.That(firstMatch).IsSameReferenceAs(first);
        await Assert.That(secondMatch).IsSameReferenceAs(second);
    }

    [Test]
    public async Task FindMatch_ExactMatchPreferredOverDifferingSourceSibling()
    {
        // A source-only sibling with differing metadata must not be consumed while the exact match is still
        // available, otherwise the item's physical fields would be rewritten to the wrong title.
        var exact = new DiscTitle(1, "00800.mpls", "1", 12, "20 GB");
        var sibling = new DiscTitle(2, "00800.mpls", "2", 8, "10 GB");
        var titles = new[] { sibling, exact };

        var match = FindMatch(titles, new HashSet<DiscTitle>(), "00800.mpls", "1", 12, "20 GB");

        await Assert.That(match).IsSameReferenceAs(exact);
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

    // Index mirrors the generated Titles record's identity: two titles cut from one playlist share physical
    // metadata but remain distinct records (and distinct HashSet entries) via their index.
    private sealed record DiscTitle(int Index, string Source, string SegmentMap, int ChapterCount, string Size);
}
