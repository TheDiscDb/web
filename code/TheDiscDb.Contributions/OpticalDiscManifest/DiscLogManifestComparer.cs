using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using MakeMkv;

namespace TheDiscDb.Contributions.OpticalDiscManifest;

public enum DiscLogManifestComparisonStatus
{
    Match,
    Mismatch,
    DifferentDisc,
    Error
}

public static partial class DiscLogManifestComparer
{
    private static readonly JsonSerializerOptions DifferencesJsonOptions = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public static DiscLogManifestComparisonResult Compare(
        DiscInfo logDiscInfo,
        string logText,
        OpticalDiscManifestDocument manifest,
        DiscInfo manifestDiscInfo,
        string? expectedContentHash = null)
    {
        ArgumentNullException.ThrowIfNull(logDiscInfo);
        ArgumentNullException.ThrowIfNull(logText);
        ArgumentNullException.ThrowIfNull(manifest);
        ArgumentNullException.ThrowIfNull(manifestDiscInfo);

        try
        {
            string? manifestContentHash = OpticalDiscManifestMapper.GetContentHash(manifest);
            string? normalizedExpectedHash = NormalizeHash(expectedContentHash);
            string? normalizedManifestHash = NormalizeHash(manifestContentHash);
            var differences = new DiscLogManifestDifferences
            {
                ExpectedContentHash = normalizedExpectedHash,
                ManifestContentHash = normalizedManifestHash,
                MakeMkvVersion = ExtractMakeMkvVersion(logText)
            };
            ReadScanDiagnostics(manifest, differences);
            CheckArtifactConsistency(logDiscInfo, manifest, differences);

            if (!string.IsNullOrEmpty(normalizedExpectedHash)
                && !string.IsNullOrEmpty(normalizedManifestHash)
                && !string.Equals(normalizedExpectedHash, normalizedManifestHash, StringComparison.OrdinalIgnoreCase))
            {
                differences.DifferentDisc = true;
                return CreateResult(
                    DiscLogManifestComparisonStatus.DifferentDisc,
                    logDiscInfo,
                    manifest,
                    manifestDiscInfo,
                    matchedTitleCount: 0,
                    orderMatches: false,
                    differences);
            }

            var logTitles = CollectTitles(logDiscInfo);
            var manifestTitles = CollectTitles(manifestDiscInfo);
            var anglePerspectiveKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var duplicate in logTitles.DuplicateKeys)
            {
                var matchingManifestModels = manifest.Disc?.Titles?
                    .Where(title => string.Equals(
                        ManifestSourceKey(title),
                        duplicate,
                        StringComparison.OrdinalIgnoreCase))
                    .ToArray() ?? [];
                if (manifestTitles.TitlesByKey.TryGetValue(duplicate, out var matchingManifestTitles)
                    && matchingManifestTitles.Count == 1
                    && matchingManifestModels.Length == 1
                    && logTitles.TitlesByKey.TryGetValue(duplicate, out var matchingLogTitles)
                    && TryResolveAnglePerspectiveTitles(
                        duplicate,
                        matchingLogTitles,
                        matchingManifestModels[0]))
                {
                    anglePerspectiveKeys.Add(duplicate);
                    differences.ArtifactWarnings.Add(
                        $"Playlist {duplicate} has complete MakeMKV title rows for its explicitly declared alternate angles. The comparison checks chapter count and length, but skips segment map, size and display size because the manifest represents all angle alternatives in one title.");
                    continue;
                }

                differences.TitleDifferences.Add(new TitleDifference(duplicate, "DuplicateLogTitle", "duplicate", null));
            }

            foreach (var duplicate in manifestTitles.DuplicateKeys)
            {
                differences.TitleDifferences.Add(new TitleDifference(duplicate, "DuplicateManifestTitle", null, "duplicate"));
            }

            var matchedKeys = logTitles.ByKey.Keys
                .Where(key => manifestTitles.ByKey.ContainsKey(key))
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            foreach (var key in logTitles.ByKey.Keys.Where(key => !manifestTitles.ByKey.ContainsKey(key)).OrderBy(key => key, StringComparer.OrdinalIgnoreCase))
            {
                differences.LogOnlyTitles.Add(key);
            }

            foreach (var key in manifestTitles.ByKey.Keys.Where(key => !logTitles.ByKey.ContainsKey(key)).OrderBy(key => key, StringComparer.OrdinalIgnoreCase))
            {
                differences.ManifestOnlyTitles.Add(key);
            }

            foreach (var key in matchedKeys.OrderBy(key => key, StringComparer.OrdinalIgnoreCase))
            {
                Title logTitle = logTitles.ByKey[key];
                Title manifestTitle = manifestTitles.ByKey[key];
                if (anglePerspectiveKeys.Contains(key))
                {
                    CompareAnglePerspectiveTitle(key, logTitle, manifestTitle, differences);
                }
                else
                {
                    CompareTitle(key, logTitle, manifestTitle, differences);
                }
            }

            var logOrder = logTitles.OrderedKeys.Where(matchedKeys.Contains)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();
            var manifestOrder = manifestTitles.OrderedKeys.Where(matchedKeys.Contains).ToArray();
            bool orderMatches = logOrder.SequenceEqual(manifestOrder, StringComparer.OrdinalIgnoreCase);
            if (!orderMatches)
            {
                differences.LogOrder = logOrder;
                differences.ManifestOrder = manifestOrder;
            }

            bool titleSetsEqual = differences.LogOnlyTitles.Count == 0
                && differences.ManifestOnlyTitles.Count == 0;
            bool hardDifferences = differences.TitleDifferences.Count > 0;
            var status = titleSetsEqual && !hardDifferences
                ? DiscLogManifestComparisonStatus.Match
                : DiscLogManifestComparisonStatus.Mismatch;

            return CreateResult(status, logDiscInfo, manifest, manifestDiscInfo, matchedKeys.Count, orderMatches, differences);
        }
        catch (Exception ex)
        {
            var differences = new DiscLogManifestDifferences
            {
                Error = ex.Message
            };
            return CreateResult(
                DiscLogManifestComparisonStatus.Error,
                logDiscInfo,
                manifest,
                manifestDiscInfo,
                matchedTitleCount: 0,
                orderMatches: false,
                differences);
        }
    }

    private static void CompareAnglePerspectiveTitle(
        string key,
        Title logTitle,
        Title manifestTitle,
        DiscLogManifestDifferences differences)
    {
        AddHardDifference(differences, key, "ChapterCount", logTitle.ChapterCount.ToString(), manifestTitle.ChapterCount.ToString());
        AddHardDifference(differences, key, "Length", logTitle.Length ?? string.Empty, manifestTitle.Length ?? string.Empty);
    }

    private static bool TryResolveAnglePerspectiveTitles(
        string sourceKey,
        IReadOnlyList<Title> logTitles,
        OpticalDiscManifestTitle manifestTitle)
    {
        if (!sourceKey.EndsWith(".mpls", StringComparison.OrdinalIgnoreCase)
            || logTitles.Count < 2
            || !TryGetAnglePerspectiveChoices(manifestTitle, out var choices, out long perspectiveCount)
            || perspectiveCount != logTitles.Count)
        {
            return false;
        }

        var seenPerspectives = new HashSet<string>(StringComparer.Ordinal);
        Title? representative = null;
        foreach (Title title in logTitles)
        {
            string[] segments = (title.SegmentMap ?? string.Empty)
                .Split(',', StringSplitOptions.None)
                .Select(segment => segment.Trim())
                .ToArray();
            if (segments.Length != choices.Count
                || segments.Where((segment, index) => !choices[index].Contains(segment)).Any()
                || !seenPerspectives.Add(string.Join(",", segments)))
            {
                return false;
            }

            if (representative is null)
            {
                representative = title;
            }
            else if (title.ChapterCount != representative.ChapterCount
                || !string.Equals(title.Length, representative.Length, StringComparison.Ordinal))
            {
                return false;
            }
        }

        return representative is not null && seenPerspectives.Count == perspectiveCount;
    }

    private static bool TryGetAnglePerspectiveChoices(
        OpticalDiscManifestTitle manifestTitle,
        out IReadOnlyList<HashSet<string>> choices,
        out long perspectiveCount)
    {
        choices = [];
        perspectiveCount = 1;
        if (manifestTitle.Segments.Count == 0
            || manifestTitle.Segments.Any(segment => segment.DependentClip is not null))
        {
            return false;
        }

        var segmentChoices = new List<HashSet<string>>();
        bool foundAngleGroup = false;
        for (int i = 0; i < manifestTitle.Segments.Count;)
        {
            OpticalDiscManifestSegment segment = manifestTitle.Segments[i];
            string? clip = OpticalDiscManifestMapper.NormalizeClipId(segment.Clip);
            if (clip is null)
            {
                return false;
            }

            if (segment.Angle is null)
            {
                segmentChoices.Add(new HashSet<string>([clip], StringComparer.Ordinal));
                i++;
                continue;
            }

            if (segment.StartSeconds is null || segment.DurationSeconds is null)
            {
                return false;
            }

            var angleGroup = new List<OpticalDiscManifestSegment>();
            while (i < manifestTitle.Segments.Count
                && manifestTitle.Segments[i].Angle is not null
                && manifestTitle.Segments[i].StartSeconds == segment.StartSeconds
                && manifestTitle.Segments[i].DurationSeconds == segment.DurationSeconds)
            {
                angleGroup.Add(manifestTitle.Segments[i]);
                i++;
            }

            int[] angles = angleGroup.Select(item => item.Angle!.Value).Order().ToArray();
            if (angleGroup.Count < 2
                || !angles.SequenceEqual(Enumerable.Range(1, angleGroup.Count))
                || angleGroup.Select(item => OpticalDiscManifestMapper.NormalizeClipId(item.Clip)).Any(item => item is null)
                || angleGroup.Select(item => OpticalDiscManifestMapper.NormalizeClipId(item.Clip)).Distinct(StringComparer.Ordinal).Count() != angleGroup.Count)
            {
                return false;
            }

            var angleChoices = angleGroup
                .Select(item => OpticalDiscManifestMapper.NormalizeClipId(item.Clip)!)
                .ToHashSet(StringComparer.Ordinal);
            if (perspectiveCount > long.MaxValue / angleChoices.Count)
            {
                return false;
            }

            segmentChoices.Add(angleChoices);
            foundAngleGroup = true;
            perspectiveCount *= angleChoices.Count;
        }

        if (!foundAngleGroup)
        {
            return false;
        }

        choices = segmentChoices;
        return true;
    }

    private static string? ManifestSourceKey(OpticalDiscManifestTitle title)
    {
        if (title.Source is not { Path: { } path } source || string.IsNullOrWhiteSpace(path))
        {
            return null;
        }

        string? fileName = NormalizeFileName(path);
        return source.Part is > 0 && fileName is not null
            ? $"{fileName}({source.Part.Value.ToString(CultureInfo.InvariantCulture)})"
            : fileName;
    }

    private static string? NormalizeFileName(string path)
    {
        string normalized = path.Replace('\\', '/');
        int separator = normalized.LastIndexOf('/');
        string name = separator >= 0 ? normalized[(separator + 1)..] : normalized;
        return name.Length == 0 ? null : name;
    }

    public static string? ExtractMakeMkvVersion(string logText)
    {
        foreach (string line in logText.Split(["\r\n", "\n"], StringSplitOptions.RemoveEmptyEntries))
        {
            if (!line.StartsWith("MSG:", StringComparison.Ordinal))
            {
                continue;
            }

            MessageLogLine message;
            try
            {
                message = MessageLogLine.Parse(line);
            }
            catch
            {
                continue;
            }

            if (message.Code == "1005" || VersionRegex().IsMatch(message.Message ?? string.Empty))
            {
                var match = VersionRegex().Match(message.Message ?? string.Empty);
                if (match.Success)
                {
                    return match.Groups[1].Value;
                }
            }
        }

        return null;
    }

    public static string SerializeDifferences(DiscLogManifestDifferences differences)
        => JsonSerializer.Serialize(differences, DifferencesJsonOptions);

    private static void ReadScanDiagnostics(
        OpticalDiscManifestDocument manifest,
        DiscLogManifestDifferences differences)
    {
        if (manifest.Extensions?.TryGetValue(
            "thediscdb.optical-disc-manifest/scan-diagnostics", out var diagnostics) != true)
        {
            return;
        }

        try
        {
            var parsed = diagnostics.Deserialize<List<OpticalDiscScanDiagnostic>>(
                OpticalDiscManifestDocument.SerializerOptions);
            if (parsed is null || parsed.Any(item => item is null
                || string.IsNullOrWhiteSpace(item.Code)
                || string.IsNullOrWhiteSpace(item.Message)
                || item.Severity is not ("info" or "warning" or "error")))
            {
                differences.ArtifactWarnings.Add("The scan diagnostics extension contains invalid diagnostic entries.");
                return;
            }

            differences.ScanDiagnostics = parsed;
        }
        catch (JsonException ex)
        {
            differences.ArtifactWarnings.Add($"The scan diagnostics extension could not be read: {ex.Message}");
        }
    }

    private static void CheckArtifactConsistency(
        DiscInfo log,
        OpticalDiscManifestDocument manifest,
        DiscLogManifestDifferences differences)
    {
        if (manifest.Disc is not { Format: "blu-ray" or "uhd-blu-ray" } disc
            || disc.Files.Count == 0)
        {
            return;
        }

        var files = disc.Files.Where(file => !string.IsNullOrWhiteSpace(file.Path))
            .GroupBy(file => file.Path!.Replace('\\', '/'), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);
        var checkedSources = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var title in log.Titles)
        {
            string? source = SourceKey(title);
            if (source is null || !BluRaySourceRegex().IsMatch(source) || !checkedSources.Add(source))
            {
                continue;
            }

            string directory = source.EndsWith(".mpls", StringComparison.OrdinalIgnoreCase)
                ? "PLAYLIST" : "STREAM";
            if (!files.TryGetValue($"BDMV/{directory}/{source}", out var file))
            {
                differences.ArtifactWarnings.Add(
                    $"Log source {source} is absent from the scan file inventory. The log and selected folder may describe different or incomplete disc sources.");
            }
            else if (directory == "STREAM" && title.Size > 0 && title.Size != file.SizeBytes)
            {
                differences.ArtifactWarnings.Add(
                    $"Log stream {source} reports {title.Size} bytes, but the scan file inventory reports {file.SizeBytes} bytes. Verify that the log and selected folder describe the same disc source.");
            }
        }
    }

    private static DiscLogManifestComparisonResult CreateResult(
        DiscLogManifestComparisonStatus status,
        DiscInfo logDiscInfo,
        OpticalDiscManifestDocument manifest,
        DiscInfo manifestDiscInfo,
        int matchedTitleCount,
        bool orderMatches,
        DiscLogManifestDifferences differences)
        => new(
            status,
            manifest.Disc?.Format,
            manifest.Producer?.Name,
            manifest.Producer?.Version,
            differences.MakeMkvVersion,
            logDiscInfo.Titles.Count,
            manifestDiscInfo.Titles.Count,
            matchedTitleCount,
            orderMatches,
            differences,
            SerializeDifferences(differences));

    private static void CompareTitle(string key, Title logTitle, Title manifestTitle, DiscLogManifestDifferences differences)
    {
        AddHardDifference(differences, key, "ChapterCount", logTitle.ChapterCount.ToString(), manifestTitle.ChapterCount.ToString());
        AddHardDifference(differences, key, "SizeBytes", logTitle.Size.ToString(), manifestTitle.Size.ToString());
        AddHardDifference(differences, key, "Length", logTitle.Length ?? string.Empty, manifestTitle.Length ?? string.Empty);
        AddHardDifference(differences, key, "SegmentMap", logTitle.SegmentMap ?? string.Empty, manifestTitle.SegmentMap ?? string.Empty);

        if (!string.Equals(logTitle.DisplaySize ?? string.Empty, manifestTitle.DisplaySize ?? string.Empty, StringComparison.Ordinal))
        {
            differences.SoftDifferences.Add(new TitleDifference(
                key,
                "DisplaySize",
                logTitle.DisplaySize,
                manifestTitle.DisplaySize));
        }
    }

    private static void AddHardDifference(
        DiscLogManifestDifferences differences,
        string key,
        string field,
        string logValue,
        string manifestValue)
    {
        if (!string.Equals(logValue, manifestValue, StringComparison.Ordinal))
        {
            differences.TitleDifferences.Add(new TitleDifference(key, field, logValue, manifestValue));
        }
    }

    private static TitleCollection CollectTitles(DiscInfo discInfo)
    {
        var byKey = new Dictionary<string, Title>(StringComparer.OrdinalIgnoreCase);
        var titlesByKey = new Dictionary<string, List<Title>>(StringComparer.OrdinalIgnoreCase);
        var orderedKeys = new List<string>();
        var duplicateKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var title in discInfo.Titles)
        {
            string? key = SourceKey(title);
            if (string.IsNullOrWhiteSpace(key))
            {
                duplicateKeys.Add($"<missing:{title.Index}>");
                continue;
            }

            orderedKeys.Add(key);
            if (!titlesByKey.TryGetValue(key, out var titles))
            {
                titles = [];
                titlesByKey.Add(key, titles);
            }

            titles.Add(title);
            if (!byKey.TryAdd(key, title))
            {
                duplicateKeys.Add(key);
            }
        }

        return new TitleCollection(
            byKey,
            titlesByKey.ToDictionary(pair => pair.Key, pair => (IReadOnlyList<Title>)pair.Value, StringComparer.OrdinalIgnoreCase),
            orderedKeys,
            duplicateKeys);
    }

    // DVD titles are keyed by title number, which MakeMKV may zero-pad ("01") and the scan does not ("1").
    private static string? SourceKey(Title title)
    {
        if (string.IsNullOrWhiteSpace(title.Playlist))
        {
            return null;
        }

        string key = title.Playlist.Trim();
        return key.All(char.IsAsciiDigit) ? key.TrimStart('0') is { Length: > 0 } trimmed ? trimmed : "0" : key;
    }

    private static string? NormalizeHash(string? hash) => string.IsNullOrWhiteSpace(hash) ? null : hash.Trim();

    [GeneratedRegex(@"MakeMKV\s+v([0-9][^""\s]*)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex VersionRegex();

    [GeneratedRegex(@"^[0-9]{5}\.(?:mpls|m2ts)$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex BluRaySourceRegex();
}

public sealed record DiscLogManifestComparisonResult(
    DiscLogManifestComparisonStatus Status,
    string? Format,
    string? ProducerName,
    string? ProducerVersion,
    string? MakeMkvVersion,
    int LogTitleCount,
    int ManifestTitleCount,
    int MatchedTitleCount,
    bool OrderMatches,
    DiscLogManifestDifferences Differences,
    string DifferencesJson);

public sealed class DiscLogManifestDifferences
{
    public string? ExpectedContentHash { get; set; }

    public string? ManifestContentHash { get; set; }

    public bool DifferentDisc { get; set; }

    public string? MakeMkvVersion { get; set; }

    public List<string> LogOnlyTitles { get; set; } = [];

    public List<string> ManifestOnlyTitles { get; set; } = [];

    public List<TitleDifference> TitleDifferences { get; set; } = [];

    public List<TitleDifference> SoftDifferences { get; set; } = [];

    public List<string> ArtifactWarnings { get; set; } = [];

    public List<OpticalDiscScanDiagnostic> ScanDiagnostics { get; set; } = [];

    public IReadOnlyList<string>? LogOrder { get; set; }

    public IReadOnlyList<string>? ManifestOrder { get; set; }

    public string? Error { get; set; }
}

public sealed record TitleDifference(string SourceKey, string Field, string? LogValue, string? ManifestValue);

internal sealed record TitleCollection(
    Dictionary<string, Title> ByKey,
    IReadOnlyDictionary<string, IReadOnlyList<Title>> TitlesByKey,
    IReadOnlyList<string> OrderedKeys,
    IReadOnlySet<string> DuplicateKeys);
