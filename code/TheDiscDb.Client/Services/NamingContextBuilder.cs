using TheDiscDb.InputModels;
using TheDiscDb.Naming;

namespace TheDiscDb.Client.Services;

/// <summary>
/// Builds <see cref="NamingContext"/> instances from StrawberryShake GraphQL types
/// used on the disc detail page.
/// </summary>
public static class NamingContextBuilder
{
    /// <summary>
    /// Creates a <see cref="NamingContext"/> for a single disc title row.
    /// </summary>
    /// <param name="mediaItem">The parent media item (movie or series).</param>
    /// <param name="disc">The disc containing the title.</param>
    /// <param name="title">The disc title/track row.</param>
    /// <param name="year">The release year (from media item or release).</param>
    /// <param name="tmdbId">TMDb external ID, if available.</param>
    /// <param name="imdbId">IMDb external ID, if available.</param>
    /// <param name="tvdbId">TVDB external ID, if available.</param>
    public static NamingContext Build(
        IDisplayItem mediaItem,
        IDisc disc,
        IDiscItem title,
        string? year,
        string? tmdbId = null,
        string? imdbId = null,
        string? tvdbId = null)
    {
        string? yearStr = !string.IsNullOrWhiteSpace(year) ? year : null;
        string? resolution = ResolveResolutionFromFormat(disc.Format);
        string? seasonNumber = PadNumber(title.Season);
        string? episodeNumber = PadNumber(title.Episode);

        bool isMainMovie = string.Equals(title.ItemType, "MainMovie", StringComparison.OrdinalIgnoreCase);

        // {title} always maps to the disc item's own title (the Description column)
        string? itemTitle = title.Description;

        // {fulltitle} is "MediaName (Year)" for MainMovie, otherwise same as {title}
        string? fullTitle = isMainMovie
            ? BuildFullTitle(mediaItem.Title, yearStr)
            : itemTitle;

        return new NamingContext
        {
            Title = itemTitle,
            Year = yearStr,
            FullTitle = fullTitle,
            Format = disc.Format,
            Resolution = resolution,
            TmdbId = tmdbId,
            ImdbId = imdbId,
            TvdbId = tvdbId,
            SeasonNumber = seasonNumber,
            EpisodeNumber = episodeNumber,
            EpisodeName = itemTitle,
            ExtraType = MapExtraType(title.ItemType),
        };
    }

    private static string? BuildFullTitle(string? title, string? year)
    {
        if (string.IsNullOrWhiteSpace(title))
        {
            return null;
        }

        return !string.IsNullOrWhiteSpace(year) ? $"{title} ({year})" : title;
    }

    private static string? PadNumber(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        return int.TryParse(value, out int number) ? number.ToString("D2") : value;
    }

    /// <summary>
    /// Derives resolution from the disc format name.
    /// </summary>
    private static string? ResolveResolutionFromFormat(string? format)
    {
        if (string.IsNullOrWhiteSpace(format))
        {
            return null;
        }

        return format.ToLowerInvariant() switch
        {
            "4k ultra hd" or "uhd" or "4k uhd" or "ultra hd blu-ray" => "2160p",
            "blu-ray" or "bluray" or "bd" => "1080p",
            "dvd" => "480p",
            "hd dvd" or "hd-dvd" => "720p",
            _ => null,
        };
    }

    /// <summary>
    /// Maps item types to Plex/Jellyfin-friendly extra type suffixes.
    /// </summary>
    private static string? MapExtraType(string? itemType)
    {
        if (string.IsNullOrWhiteSpace(itemType))
        {
            return null;
        }

        return itemType.ToLowerInvariant() switch
        {
            "featurette" or "featurettes" => "featurette",
            "behind the scenes" or "behindthescenes" => "behindthescenes",
            "deleted scene" or "deleted scenes" or "deleted" => "deleted",
            "interview" or "interviews" => "interview",
            "trailer" or "trailers" => "trailer",
            "scene" or "scenes" => "scene",
            "short" or "shorts" => "short",
            _ => itemType,
        };
    }
}
