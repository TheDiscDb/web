using System.Globalization;
using MakeMkv;

namespace TheDiscDb.Contributions.OpticalDiscManifest;

public sealed record IdentifySimilarityMeasurement(long Matching, long Total)
{
    public double? Percent => Total == 0 ? null : 100d * Matching / Total;
    public string Display => Percent is double value ? $"{value:F1}% ({Matching}/{Total})" : "Not comparable";
}

public sealed record IdentifySimilarityDifference(int LogRow, int ManifestRow, string Field, string LogValue, string ManifestValue);

public sealed record IdentifySimilarityWeights(int ChoiceCoverage, int RowPresentation, int Ordering, int Labeling);

public sealed record IdentifySimilarityScore(
    int Version,
    int MatchedChoices,
    IReadOnlyList<string> MissingChoices,
    IReadOnlyList<string> ExtraChoices,
    IdentifySimilarityMeasurement ChoiceCoverage,
    IdentifySimilarityMeasurement RowPresentation,
    IdentifySimilarityMeasurement Ordering,
    IdentifySimilarityMeasurement Labeling,
    IReadOnlyList<IdentifySimilarityDifference> Differences)
{
    public const int CurrentVersion = 1;
    public const string Formula = "40% choice coverage + 25% row presentation + 15% ordering + 20% labeling";
    public const string Scope = "Source-identity aligned choices (duplicate occurrences paired in acquisition order); "
        + "missing/extra occurrences fail every component. No main-feature weighting. Strict identify v1 remains separate.";
    public IdentifySimilarityWeights Weights => new(40, 25, 15, 20);
    public string ScopeDescription => Scope;
    public string Denominators => "U = matched + missing + extra occurrences; coverage U; presentation 7U; "
        + "labeling 7U; ordering U(U-1)/2 (sole aligned choice 1/1). No aligned choices earn no matches.";
    public bool IsMeasured => Version == CurrentVersion && ChoiceCoverage.Total > 0
        && RowPresentation.Total > 0 && Ordering.Total > 0 && Labeling.Total > 0;
    public double? Overall => IsMeasured
        ? .40 * ChoiceCoverage.Percent!.Value + .25 * RowPresentation.Percent!.Value
            + .15 * Ordering.Percent!.Value + .20 * Labeling.Percent!.Value
        : null;
    public string Display => Overall is double value ? $"{value:F1}%" : "Not measured";
    public string Summary => $"{Display}; coverage {ChoiceCoverage.Display}; presentation {RowPresentation.Display}; "
        + $"ordering {Ordering.Display}; labeling {Labeling.Display}; missing {MissingChoices.Count}, extra {ExtraChoices.Count}";
}

public static class IdentifySimilarityComparer
{
    public static IdentifySimilarityScore? Compare(DiscInfo log, DiscInfo scan)
    {
        if (log.Titles.Count == 0 || scan.Titles.Count == 0)
        {
            return null;
        }

        var available = new Dictionary<string, Queue<int>>(StringComparer.OrdinalIgnoreCase);
        for (int i = 0; i < scan.Titles.Count; i++)
        {
            string? key = Source(scan.Titles[i]);
            if (key is null) continue;
            if (!available.TryGetValue(key, out var indices)) available[key] = indices = new();
            indices.Enqueue(i);
        }

        var pairs = new List<(int Log, int Scan)>();
        var missing = new List<string>();
        var used = new HashSet<int>();
        for (int i = 0; i < log.Titles.Count; i++)
        {
            string? key = Source(log.Titles[i]);
            if (key is not null && available.TryGetValue(key, out var indices) && indices.Count > 0)
            {
                int index = indices.Dequeue();
                pairs.Add((i, index));
                used.Add(index);
            }
            else missing.Add(Choice(log.Titles[i], i));
        }
        var extra = scan.Titles.Select((title, i) => (title, i)).Where(t => !used.Contains(t.i))
            .Select(t => Choice(t.title, t.i)).ToArray();
        long union = pairs.Count + missing.Count + extra.Length;
        long presentation = 0, labeling = 0;
        bool logComments = IdentifyTitlePresentation.HasCommentColumn(log.Titles.Select(t => t.JavaComment));
        bool scanComments = IdentifyTitlePresentation.HasCommentColumn(scan.Titles.Select(t => t.JavaComment));
        var differences = new List<IdentifySimilarityDifference>();
        foreach (var pair in pairs)
        {
            Title left = log.Titles[pair.Log], right = scan.Titles[pair.Scan];
            Check("ChapterCount", left.ChapterCount, right.ChapterCount, false);
            Check("DisplaySize", left.DisplaySize ?? "", right.DisplaySize ?? "", false);
            Check("Length", left.Length ?? "", right.Length ?? "", false);
            Check("Playlist", left.Playlist ?? "", right.Playlist ?? "", false);
            // Truncated text and full tooltip represent one segment-map concept, not two votes.
            Check("SegmentMap", left.SegmentMap ?? "", right.SegmentMap ?? "", false);
            Check("CommentColumnVisible", logComments, scanComments, false);
            Check("Comment", logComments ? left.JavaComment ?? "" : "", scanComments ? right.JavaComment ?? "" : "", false);
            Check("LabelChapters", IdentifyTitlePresentation.CanLabelChapters(left.ChapterCount),
                IdentifyTitlePresentation.CanLabelChapters(right.ChapterCount), true);
            Tracks("Audio", IdentifyTitlePresentation.IsAudio,
                s => IdentifyTitlePresentation.AudioName(s.Name, s.AudioType));
            Tracks("Subtitles", IdentifyTitlePresentation.IsSubtitle, s => IdentifyTitlePresentation.SubtitleName(s.Name));

            void Tracks(string kind, Func<string?, bool> predicate, Func<Segment, string> name)
            {
                string[] a = left.Segments.Where(s => predicate(s.Type)).Select(name).ToArray();
                string[] b = right.Segments.Where(s => predicate(s.Type)).Select(name).ToArray();
                Check($"Label{kind}", a.Length > 0, b.Length > 0, true);
                Check($"{kind}DialogCount", a.Length, b.Length, true);
                Check($"{kind}DialogNames", System.Text.Json.JsonSerializer.Serialize(a),
                    System.Text.Json.JsonSerializer.Serialize(b), true);
            }

            void Check<T>(string field, T a, T b, bool label)
            {
                if (EqualityComparer<T>.Default.Equals(a, b))
                {
                    if (label) labeling++; else presentation++;
                }
                else differences.Add(new(pair.Log + 1, pair.Scan + 1, field,
                    Convert.ToString(a, CultureInfo.InvariantCulture) ?? "", Convert.ToString(b, CultureInfo.InvariantCulture) ?? ""));
            }
        }

        // Fenwick tree counts concordant aligned pairs with O(n) memory and O(n log n) time.
        long concordant = 0;
        var tree = new int[scan.Titles.Count + 1];
        foreach (var pair in pairs)
        {
            for (int i = pair.Scan; i > 0; i -= i & -i) concordant += tree[i];
            for (int i = pair.Scan + 1; i < tree.Length; i += i & -i) tree[i]++;
        }
        long orderTotal = union == 1 ? 1 : union * (union - 1) / 2;
        if (union == 1 && pairs.Count == 1) concordant = 1;
        return new(IdentifySimilarityScore.CurrentVersion, pairs.Count, missing, extra,
            new(pairs.Count, union), new(presentation, 7 * union), new(concordant, orderTotal),
            new(labeling, 7 * union), differences);
    }

    private static string Choice(Title title, int index) => $"Row {index + 1}: {title.Playlist ?? "(unknown source)"}";

    private static string? Source(Title title)
    {
        string? key = title.Playlist?.Trim();
        if (string.IsNullOrEmpty(key)) return null;
        // DVD numeric padding is identity-equivalent, but remains a presentation difference.
        return key.All(char.IsAsciiDigit) && int.TryParse(key, NumberStyles.None, CultureInfo.InvariantCulture, out int number)
            ? number.ToString(CultureInfo.InvariantCulture) : key;
    }
}
