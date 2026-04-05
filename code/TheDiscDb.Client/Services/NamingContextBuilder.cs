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
        string? fullTitle = BuildFullTitle(mediaItem.Title, yearStr);

        string? seasonNumber = PadNumber(title.Season);
        string? episodeNumber = PadNumber(title.Episode);

        return new NamingContext
        {
            Title = mediaItem.Title,
            Year = yearStr,
            FullTitle = fullTitle,
            Format = disc.Format,
            TmdbId = tmdbId,
            ImdbId = imdbId,
            TvdbId = tvdbId,
            SeasonNumber = seasonNumber,
            EpisodeNumber = episodeNumber,
            EpisodeName = title.Description,
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
