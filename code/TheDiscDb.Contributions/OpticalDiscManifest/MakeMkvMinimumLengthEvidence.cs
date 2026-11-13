using System.Globalization;
using System.Text.Json.Serialization;
using MakeMkv;

namespace TheDiscDb.Contributions.OpticalDiscManifest;

[JsonConverter(typeof(JsonStringEnumConverter<MakeMkvMinimumLengthStatus>))]
public enum MakeMkvMinimumLengthStatus
{
    Unknown,
    Zero,
    NonZero,
    Conflicting,
    Malformed
}

public sealed record MakeMkvMinimumLengthEvidence(
    MakeMkvMinimumLengthStatus Status,
    long? ThresholdSeconds,
    string? Reason)
{
    public bool ExcludesReference => Status == MakeMkvMinimumLengthStatus.NonZero;
}

public static class MakeMkvMinimumLengthAnalyzer
{
    public static MakeMkvMinimumLengthEvidence Analyze(string logText)
    {
        ArgumentNullException.ThrowIfNull(logText);

        var thresholds = new List<long>();
        bool malformed = false;
        foreach (string line in logText.Split(["\r\n", "\n"], StringSplitOptions.RemoveEmptyEntries))
        {
            if (!line.StartsWith("MSG:", StringComparison.Ordinal))
            {
                continue;
            }

            var csv = new CsvEnumerator(line[4..]);
            if (!string.Equals(csv.GetString(), "3025", StringComparison.Ordinal))
            {
                continue;
            }

            string? flags = csv.GetString();
            string? countText = csv.GetString();
            string? message = csv.GetString();
            string? messageTemplate = csv.GetString();
            var parameters = new List<string>();
            while (csv.GetString() is { } parameter)
            {
                parameters.Add(parameter);
            }

            if (flags is null || message is null || messageTemplate is null
                || !int.TryParse(countText, NumberStyles.None, CultureInfo.InvariantCulture, out int count)
                || count != parameters.Count || count != 2
                || !long.TryParse(parameters[^1], NumberStyles.Integer, CultureInfo.InvariantCulture, out long threshold)
                || threshold < 0)
            {
                malformed = true;
                continue;
            }

            thresholds.Add(threshold);
        }

        if (malformed)
        {
            return new(MakeMkvMinimumLengthStatus.Malformed, null,
                "At least one MSG:3025 row is malformed or has an invalid threshold.");
        }

        if (thresholds.Count == 0)
        {
            return new(MakeMkvMinimumLengthStatus.Unknown, null,
                "The log contains no MSG:3025 threshold evidence; its minimum length is unknown.");
        }

        long[] distinctThresholds = thresholds.Distinct().Order().ToArray();
        if (distinctThresholds.Length != 1)
        {
            return new(MakeMkvMinimumLengthStatus.Conflicting, null,
                "MSG:3025 rows report conflicting minimum-length thresholds.");
        }

        long observed = distinctThresholds[0];
        return observed == 0
            ? new(MakeMkvMinimumLengthStatus.Zero, observed, "MSG:3025 rows report a zero-second minimum length.")
            : new(MakeMkvMinimumLengthStatus.NonZero, observed,
                $"MSG:3025 rows report a nonzero minimum length of {observed.ToString(CultureInfo.InvariantCulture)} seconds.");
    }
}
