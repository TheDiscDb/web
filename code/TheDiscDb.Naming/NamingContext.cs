using System;
using System.Collections.Generic;
using System.Linq;
using TheDiscDb.InputModels;

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

    /// <summary>
    /// Creates a <see cref="NamingContext"/> from a <see cref="MediaItem"/>.
    /// Populates title, year, fullTitle, and external IDs.
    /// </summary>
    public static NamingContext Create(MediaItem mediaItem)
    {
        ArgumentNullException.ThrowIfNull(mediaItem);

        string yearStr = mediaItem.Year > 0 ? mediaItem.Year.ToString() : null!;
        string? fullTitle = !string.IsNullOrWhiteSpace(mediaItem.FullTitle)
            ? mediaItem.FullTitle
            : BuildFullTitle(mediaItem.Title, yearStr);

        return new NamingContext
        {
            Title = mediaItem.Title,
            Year = yearStr,
            FullTitle = fullTitle,
            TmdbId = mediaItem.Externalids?.Tmdb,
            ImdbId = mediaItem.Externalids?.Imdb,
            TvdbId = mediaItem.Externalids?.Tvdb,
        };
    }

    /// <summary>
    /// Creates a <see cref="NamingContext"/> from a <see cref="MediaItem"/> and <see cref="Release"/>.
    /// Adds edition from the release type.
    /// </summary>
    public static NamingContext Create(MediaItem mediaItem, Release release)
    {
        ArgumentNullException.ThrowIfNull(release);

        return Create(mediaItem) with
        {
            Edition = release.Type,
        };
    }

    /// <summary>
    /// Creates a <see cref="NamingContext"/> from a <see cref="MediaItem"/>,
    /// <see cref="Release"/>, and <see cref="Disc"/>.
    /// Adds disc format.
    /// </summary>
    public static NamingContext Create(MediaItem mediaItem, Release release, Disc disc)
    {
        ArgumentNullException.ThrowIfNull(disc);

        return Create(mediaItem, release) with
        {
            Format = disc.Format,
        };
    }

    /// <summary>
    /// Creates a <see cref="NamingContext"/> from a <see cref="MediaItem"/>,
    /// <see cref="Release"/>, <see cref="Disc"/>, and <see cref="InputModels.Title"/>.
    /// Adds resolution, part, season/episode info, extra type, and episode name.
    /// </summary>
    public static NamingContext Create(MediaItem mediaItem, Release release, Disc disc, InputModels.Title title)
    {
        ArgumentNullException.ThrowIfNull(title);

        string? resolution = ResolveResolution(title.Tracks);
        string? part = title.Index > 0 ? $"pt{title.Index}" : null;

        string? rawSeason = !string.IsNullOrWhiteSpace(title.Item?.Season)
            ? title.Item!.Season
            : title.Season;
        string? rawEpisode = !string.IsNullOrWhiteSpace(title.Item?.Episode)
            ? title.Item!.Episode
            : title.Episode;

        string? seasonNumber = PadNumber(rawSeason);
        string? episodeNumber = PadNumber(rawEpisode);
        string? episodeName = title.Item?.Title;
        string? extraType = title.Item?.Type;

        return Create(mediaItem, release, disc) with
        {
            Resolution = resolution,
            Part = part,
            SeasonNumber = seasonNumber,
            EpisodeNumber = episodeNumber,
            EpisodeName = episodeName,
            ExtraType = extraType,
        };
    }

    /// <summary>
    /// Extracts resolution from the first video track and maps it to a friendly format
    /// (e.g., "3840x2160" → "2160p").
    /// </summary>
    internal static string? ResolveResolution(ICollection<Track>? tracks)
    {
        if (tracks is null)
        {
            return null;
        }

        string? raw = tracks.FirstOrDefault(t =>
            string.Equals(t.Type, "Video", StringComparison.OrdinalIgnoreCase))?.Resolution;

        if (string.IsNullOrWhiteSpace(raw))
        {
            return null;
        }

        int xIndex = raw.IndexOf('x', StringComparison.Ordinal);
        if (xIndex >= 0 && xIndex + 1 < raw.Length)
        {
            string heightPart = raw.Substring(xIndex + 1);
            if (int.TryParse(heightPart, out int height) && height > 0)
            {
                return $"{height}p";
            }
        }

        return raw;
    }

    /// <summary>
    /// Pads a numeric string to at least two digits (e.g., "3" → "03", "11" → "11").
    /// Returns null for null/empty/non-numeric input.
    /// </summary>
    internal static string? PadNumber(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        if (int.TryParse(value, out int number))
        {
            return number.ToString("D2");
        }

        return value;
    }

    private static string? BuildFullTitle(string? title, string? year)
    {
        if (string.IsNullOrWhiteSpace(title))
        {
            return null;
        }

        if (!string.IsNullOrWhiteSpace(year))
        {
            return $"{title} ({year})";
        }

        return title;
    }
}
