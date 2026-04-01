using System;

namespace TheDiscDb.Naming;

/// <summary>
/// Provides metadata values for template token substitution.
/// All properties are nullable — null or whitespace values are treated as missing.
/// </summary>
public sealed record NamingContext
{
    public string? Title { get; init; }
    public string? Year { get; init; }
    public string? FullTitle { get; init; }
    public string? Resolution { get; init; }
    public string? Format { get; init; }
    public string? TmdbId { get; init; }
    public string? ImdbId { get; init; }
    public string? TvdbId { get; init; }
    public string? Edition { get; init; }
    public string? Part { get; init; }
    public string? ExtraType { get; init; }
    public string? SeasonNumber { get; init; }
    public string? EpisodeNumber { get; init; }
    public string? EpisodeName { get; init; }
    public string? AirDate { get; init; }
}
