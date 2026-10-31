using System.Globalization;
using System.Text.Json.Serialization;
using MakeMkv;

namespace TheDiscDb.Contributions.OpticalDiscManifest;

public sealed record UsefulStreamLabel(string Type, int Ordinal, string Name);

public sealed record UsefulContentReference(
    string Name, string Source, string? SegmentMap, string? Duration, bool Required = false,
    bool Incidental = false, IReadOnlyList<UsefulStreamLabel>? Labels = null);

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum UsefulContentStatus { Measured, NoUsefulReferences, AcquisitionReview, NotComparable }

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum UsefulChoiceAvailability
{
    AvailableRecordedMetadata, AvailableEquivalentCandidates, AvailableWithSmallFragments,
    AvailableWithTruncatedMap, BoundaryReview, TimingReview, AngleReview, ReferenceReview, Unrepresented
}

public sealed class UsefulContentComparison
{
    public const int CurrentVersion = 1;
    public const string Scope = "Metadata-supported availability of explicitly identified choices, not playback or semantic-stream recall. "
        + "Unclassified content is not incidental; duration is not a usefulness filter. Existing parity metrics are unchanged.";

    public int Version { get; set; } = CurrentVersion;
    public UsefulContentStatus Status { get; set; }
    public string? Reason { get; set; }
    public int UnclassifiedReferenceRows { get; set; }
    public int UnclassifiedScanChoices { get; set; }
    public int IncidentalReferences { get; set; }
    public List<UsefulChoiceComparison> Choices { get; set; } = [];
    public int KnownUseful => Choices.Count;
    public int Available => Choices.Count(choice => choice.IsAvailable);
    public int RequiredUnresolved => Choices.Count(choice => choice.Required && !choice.IsAvailable);
    public bool AllKnownUsefulAvailable => Status == UsefulContentStatus.Measured && KnownUseful > 0 && Available == KnownUseful;
}

public sealed class UsefulChoiceComparison
{
    public string Name { get; set; } = "";
    public string Source { get; set; } = "";
    public bool Required { get; set; }
    public UsefulChoiceAvailability Availability { get; set; }
    public string Evidence { get; set; } = "";
    public int? LogRow { get; set; }
    public int? ScanRow { get; set; }
    public List<string> CandidateSources { get; set; } = [];
    public StreamCapabilityComparison? Streams { get; set; }
    public bool IsAvailable => Availability is UsefulChoiceAvailability.AvailableRecordedMetadata
        or UsefulChoiceAvailability.AvailableEquivalentCandidates
        or UsefulChoiceAvailability.AvailableWithSmallFragments
        or UsefulChoiceAvailability.AvailableWithTruncatedMap;
}

public sealed class StreamCapabilityComparison
{
    public bool AudioRowCountsDiffer { get; set; }
    public bool SubtitleRowCountsDiffer { get; set; }
    public bool NamedOrdinalMappingNeedsReview { get; set; }
    public int NamedLabelsNotSemanticallyVerified { get; set; }
    public int NamedOrdinalsOutsideScan { get; set; }
    public bool DeclaredClosedCaptions { get; set; }
    public bool ReferenceClosedCaptions { get; set; }
    public List<string> AudioLanguageSignals { get; set; } = [];
    public List<string> SubtitleLanguageSignals { get; set; } = [];
    public string Scope { get; set; } = "Counts and language differences are review signals, not proof of missing payload. "
        + "In-range ordinals do not establish commentary, accessibility or forced-stream correspondence.";
}

public static class UsefulContentComparer
{
    public static UsefulContentComparison Compare(
        DiscInfo log, OpticalDiscManifestDocument manifest, DiscInfo scan,
        IReadOnlyList<UsefulContentReference> references, bool differentDisc = false,
        bool acquisitionReview = false, bool scanErrors = false)
    {
        var result = new UsefulContentComparison
        {
            Status = differentDisc || scanErrors ? UsefulContentStatus.NotComparable
                : acquisitionReview ? UsefulContentStatus.AcquisitionReview
                : references.Any(reference => !reference.Incidental) ? UsefulContentStatus.Measured
                : UsefulContentStatus.NoUsefulReferences,
            Reason = differentDisc ? "The acquisitions identify different discs."
                : scanErrors ? "The scan contains error diagnostics."
                : acquisitionReview ? "Artifact consistency warnings require acquisition review; availability is not certified."
                : null,
            IncidentalReferences = references.Count(reference => reference.Incidental)
        };
        bool dvd = string.Equals(manifest.Disc?.Format, "dvd", StringComparison.OrdinalIgnoreCase);
        var usedLog = new HashSet<int>();
        var usedScan = new HashSet<int>();
        foreach (var reference in references)
        {
            var logRows = Enumerable.Range(0, log.Titles.Count)
                .Where(index => !usedLog.Contains(index)
                    && SourceKey(log.Titles[index].Playlist, dvd) == SourceKey(reference.Source, dvd)
                    && (string.IsNullOrWhiteSpace(reference.SegmentMap) || Map(reference.SegmentMap) == Map(log.Titles[index].SegmentMap))
                    && (string.IsNullOrWhiteSpace(reference.Duration) || SameDuration(reference.Duration, log.Titles[index].Length)))
                .ToArray();
            int? logRow = logRows.Length == 1 ? logRows[0] : null;
            if (logRow is not null)
            {
                usedLog.Add(logRow.Value);
            }
            if (reference.Incidental)
            {
                continue;
            }

            var choice = new UsefulChoiceComparison
            {
                Name = reference.Name, Source = reference.Source, Required = reference.Required,
                LogRow = logRow + 1, Availability = UsefulChoiceAvailability.ReferenceReview,
                Evidence = "The saved identification does not resolve to one unused reference occurrence."
            };
            result.Choices.Add(choice);
            if (logRow is null || result.Status is UsefulContentStatus.NotComparable or UsefulContentStatus.AcquisitionReview)
            {
                continue;
            }

            Title expected = log.Titles[logRow.Value];
            var candidates = Enumerable.Range(0, scan.Titles.Count)
                .Where(index => !usedScan.Contains(index))
                .Select(index => Assess(expected, scan.Titles[index], manifest, index, dvd))
                .Where(candidate => candidate is not null)
                .Select(candidate => candidate!)
                .OrderBy(candidate => candidate.Rank)
                .ThenBy(candidate => candidate.Index)
                .ToArray();
            if (candidates.Length == 0)
            {
                choice.Availability = UsefulChoiceAvailability.Unrepresented;
                choice.Evidence = "No independently supported scanner counterpart; usefulness is not inferred from duration.";
                continue;
            }

            var selected = candidates[0];
            var equivalent = candidates.Where(candidate => candidate.Rank == selected.Rank).ToArray();
            choice.Availability = selected.Availability;
            choice.Evidence = selected.Evidence;
            choice.CandidateSources = equivalent.Select(candidate => scan.Titles[candidate.Index].Playlist ?? "").ToList();
            choice.ScanRow = selected.Index + 1;
            if (choice.IsAvailable)
            {
                usedScan.Add(selected.Index);
                if (equivalent.Length > 1)
                {
                    choice.Availability = UsefulChoiceAvailability.AvailableEquivalentCandidates;
                    choice.Evidence += " Multiple recorded counterparts exist; no uniquely selected semantic presentation is asserted.";
                }
            }
            choice.Streams = CompareStreams(expected, scan.Titles[selected.Index], reference.Labels ?? []);
        }

        result.UnclassifiedReferenceRows = log.Titles.Count - usedLog.Count;
        result.UnclassifiedScanChoices = scan.Titles.Count - usedScan.Count;
        return result;
    }

    private static Candidate? Assess(Title expected, Title actual, OpticalDiscManifestDocument manifest, int index, bool dvd)
    {
        bool sameSource = SourceKey(expected.Playlist, dvd) is { Length: > 0 } key
            && key == SourceKey(actual.Playlist, dvd);
        string map = Map(expected.SegmentMap);
        string actualMap = Map(actual.SegmentMap);
        var model = index < (manifest.Disc?.Titles.Count ?? 0) ? manifest.Disc!.Titles[index] : null;
        bool angles = model?.Segments.Any(segment => segment.Angle is > 1) == true;
        bool sameMap = map.Length > 0 && map == actualMap;
        bool sameBytes = expected.Size > 0 && expected.Size == actual.Size;
        bool sameDuration = SameDuration(expected.Length, actual.Length);
        if (angles && (sameSource || sameMap))
        {
            return new(index, 20, UsefulChoiceAvailability.AngleReview, "Explicit alternate perspectives require separate review.");
        }
        if (sameMap && sameDuration && (sameSource || (!dvd && sameBytes)))
        {
            return new(index, sameSource ? 0 : 1, UsefulChoiceAvailability.AvailableRecordedMetadata,
                sameSource ? "Same source, ordered map and displayed duration; not playback validation."
                    : "Source alias with equal ordered map, positive byte size and displayed duration.");
        }
        if (!dvd && sameSource && sameBytes && sameDuration && map.EndsWith(",...", StringComparison.Ordinal)
            && actualMap.StartsWith(map[..^3], StringComparison.Ordinal))
        {
            return new(index, 2, UsefulChoiceAvailability.AvailableWithTruncatedMap,
                "Explicitly truncated reference map agrees with the scanner prefix, source, duration and positive size.");
        }
        if (!dvd && !angles && model is not null && map.Length > 0 && !map.Contains("...", StringComparison.Ordinal))
        {
            string[] clips = map.Split(',');
            string[] scanClips = actualMap.Split(',');
            for (int start = 0; start + clips.Length <= scanClips.Length; start++)
            {
                if (!clips.SequenceEqual(scanClips.Skip(start).Take(clips.Length), StringComparer.Ordinal))
                {
                    continue;
                }
                var segments = model.Segments;
                if (segments.Count != scanClips.Length || segments.Count <= clips.Length
                    || segments.Any(segment => segment.DurationSeconds is null or < 0))
                {
                    break;
                }
                double coreSeconds = segments.Skip(start).Take(clips.Length).Sum(segment => segment.DurationSeconds!.Value);
                double addedSeconds = segments.Take(start).Concat(segments.Skip(start + clips.Length))
                    .Sum(segment => segment.DurationSeconds!.Value);
                var files = manifest.Disc!.Files
                    .Where(file => file.Path?.EndsWith(".m2ts", StringComparison.OrdinalIgnoreCase) == true)
                    .GroupBy(file => OpticalDiscManifestMapper.NormalizeClipId(file.Path) ?? "", StringComparer.Ordinal)
                    .Where(group => group.Select(file => file.SizeBytes).Distinct().Count() == 1)
                    .ToDictionary(group => group.Key, group => group.First().SizeBytes, StringComparer.Ordinal);
                long bytes = 0;
                bool completeFiles = true;
                foreach (var segment in segments.Skip(start).Take(clips.Length))
                {
                    if (!files.TryGetValue(OpticalDiscManifestMapper.NormalizeClipId(segment.Clip) ?? "", out long size) || size <= 0)
                    {
                        completeFiles = false;
                        break;
                    }
                    bytes += size;
                }
                if (completeFiles && bytes == expected.Size && TrySeconds(expected.Length, out double seconds)
                    && Math.Abs(coreSeconds - seconds) <= 1 && TrySeconds(actual.Length, out double actualSeconds)
                    && Math.Abs(actualSeconds - coreSeconds - addedSeconds) <= 1)
                {
                    bool small = addedSeconds <= 1.002;
                    return new(index, small ? 3 : 10,
                        small ? UsefulChoiceAvailability.AvailableWithSmallFragments : UsefulChoiceAvailability.BoundaryReview,
                        $"Complete recorded core sequence, shared file bytes and duration; added boundaries total {addedSeconds.ToString("F6", CultureInfo.InvariantCulture)} seconds. Independent choice and playback are not inferred.");
                }
            }
        }
        return sameSource
            ? new(index, 30, sameMap ? UsefulChoiceAvailability.TimingReview : UsefulChoiceAvailability.BoundaryReview,
                "Same source has different or incomplete duration/ordered-map evidence; captures may be required.")
            : null;
    }

    private static StreamCapabilityComparison CompareStreams(Title expected, Title actual, IReadOnlyList<UsefulStreamLabel> labels)
    {
        var logAudio = Streams(expected, "Audio");
        var scanAudio = Streams(actual, "Audio");
        var logSubtitles = Streams(expected, "Subtitles");
        var scanSubtitles = Streams(actual, "Subtitles");
        var result = new StreamCapabilityComparison
        {
            AudioRowCountsDiffer = logAudio.Length != scanAudio.Length,
            SubtitleRowCountsDiffer = logSubtitles.Length != scanSubtitles.Length,
            NamedLabelsNotSemanticallyVerified = labels.Count(label => !string.IsNullOrWhiteSpace(label.Name)),
            DeclaredClosedCaptions = actual.HasDeclaredClosedCaptions,
            ReferenceClosedCaptions = logSubtitles.Any(stream => stream.Name?.Contains("Closed Captions", StringComparison.OrdinalIgnoreCase) == true),
            AudioLanguageSignals = LanguageGaps(logAudio, scanAudio),
            SubtitleLanguageSignals = LanguageGaps(logSubtitles, scanSubtitles)
        };
        foreach (var label in labels.Where(label => !string.IsNullOrWhiteSpace(label.Name)))
        {
            var logRows = Streams(expected, label.Type);
            var scanRows = Streams(actual, label.Type);
            if (label.Ordinal < 1 || label.Ordinal > logRows.Length)
            {
                result.NamedOrdinalMappingNeedsReview = true;
                continue;
            }
            if (label.Ordinal > scanRows.Length)
            {
                result.NamedOrdinalsOutsideScan++;
                result.NamedOrdinalMappingNeedsReview = true;
            }
            else if (logRows.Length != scanRows.Length
                || Signature(logRows[label.Ordinal - 1]) != Signature(scanRows[label.Ordinal - 1]))
            {
                result.NamedOrdinalMappingNeedsReview = true;
            }
        }
        return result;
    }

    private static Segment[] Streams(Title title, string type)
        => title.Segments.Where(stream => string.Equals(stream.Type, type, StringComparison.OrdinalIgnoreCase)).ToArray();

    private static List<string> LanguageGaps(IEnumerable<Segment> expected, IEnumerable<Segment> actual)
        => expected.Select(stream => LanguageCodes.Canonicalize(stream.LanguageCode))
            .OfType<string>().Except(actual.Select(stream => LanguageCodes.Canonicalize(stream.LanguageCode)).OfType<string>(),
                StringComparer.Ordinal).Order(StringComparer.Ordinal).ToList();

    private static string Signature(Segment stream)
        => $"{stream.Name?.Trim().ToLowerInvariant()}|{LanguageCodes.Canonicalize(stream.LanguageCode)}|{stream.AudioType?.Trim().ToLowerInvariant()}";

    private static string Map(string? value) => (value ?? "").Replace(" ", "", StringComparison.Ordinal).ToLowerInvariant();
    private static string FileName(string? value) => (value ?? "").Replace('\\', '/').Split('/').Last();
    private static string SourceKey(string? value, bool dvd)
        => dvd && int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int number)
            ? number.ToString(CultureInfo.InvariantCulture) : FileName(value).Trim().ToLowerInvariant();
    private static bool TrySeconds(string? value, out double seconds)
    {
        bool valid = TimeSpan.TryParse(value, CultureInfo.InvariantCulture, out var time) && time >= TimeSpan.Zero;
        seconds = time.TotalSeconds;
        return valid;
    }
    private static bool SameDuration(string? left, string? right)
        => TrySeconds(left, out double a) && TrySeconds(right, out double b) && Math.Abs(a - b) <= 1;

    private sealed record Candidate(int Index, int Rank, UsefulChoiceAvailability Availability, string Evidence);
}
