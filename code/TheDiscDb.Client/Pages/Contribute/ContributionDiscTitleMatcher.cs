namespace TheDiscDb.Client.Pages.Contribute;

internal static class ContributionDiscTitleMatcher
{
    public static TTitle? FindMatch<TTitle>(
        IEnumerable<TTitle> titles,
        ISet<TTitle> matchedTitles,
        string? source,
        string? segmentMap,
        int chapterCount,
        string? size,
        Func<TTitle, string?> sourceSelector,
        Func<TTitle, string?> segmentMapSelector,
        Func<TTitle, int> chapterCountSelector,
        Func<TTitle, string?> sizeSelector)
        where TTitle : class
    {
        var availableTitles = titles.Where(title => !matchedTitles.Contains(title)).ToList();

        if (!string.IsNullOrWhiteSpace(source))
        {
            var exactMatches = availableTitles
                .Where(title =>
                    ValuesMatch(sourceSelector(title), source) &&
                    MetadataMatches(title, segmentMap, chapterCount, size, segmentMapSelector, chapterCountSelector, sizeSelector))
                .ToList();

            if (exactMatches.Count == 1)
            {
                return exactMatches[0];
            }

            var sourceMatches = availableTitles
                .Where(title => ValuesMatch(sourceSelector(title), source))
                .ToList();

            if (sourceMatches.Count == 1)
            {
                return sourceMatches[0];
            }

            return null;
        }

        var metadataMatches = availableTitles
            .Where(title => MetadataMatches(title, segmentMap, chapterCount, size, segmentMapSelector, chapterCountSelector, sizeSelector))
            .ToList();

        return metadataMatches.Count == 1 ? metadataMatches[0] : null;
    }

    private static bool MetadataMatches<TTitle>(
        TTitle title,
        string? segmentMap,
        int chapterCount,
        string? size,
        Func<TTitle, string?> segmentMapSelector,
        Func<TTitle, int> chapterCountSelector,
        Func<TTitle, string?> sizeSelector)
    {
        return ValuesMatch(segmentMapSelector(title), segmentMap) &&
            chapterCountSelector(title) == chapterCount &&
            ValuesMatch(sizeSelector(title), size);
    }

    private static bool ValuesMatch(string? first, string? second)
    {
        return string.Equals(first, second, StringComparison.OrdinalIgnoreCase);
    }
}
