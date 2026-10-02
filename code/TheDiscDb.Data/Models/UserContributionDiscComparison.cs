namespace TheDiscDb.Web.Data;

using System;
using System.Text.Json.Serialization;

public enum UserContributionDiscComparisonStatus
{
    Match,
    Mismatch,
    DifferentDisc,
    Error
}

public class UserContributionDiscComparison : IHasId
{
    [JsonIgnore]
    public int Id { get; set; }

    public int DiscId { get; set; }

    [JsonIgnore]
    public UserContributionDisc Disc { get; set; } = default!;

    public DateTimeOffset ComparedAt { get; set; }

    public string? Format { get; set; }

    public string? ProducerName { get; set; }

    public string? ProducerVersion { get; set; }

    public string? MakeMkvVersion { get; set; }

    public string? UserAgent { get; set; }

    public int LogTitleCount { get; set; }

    public int ManifestTitleCount { get; set; }

    public int MatchedTitleCount { get; set; }

    public bool OrderMatches { get; set; }

    public UserContributionDiscComparisonStatus Status { get; set; }

    public string DifferencesJson { get; set; } = "{}";
}
