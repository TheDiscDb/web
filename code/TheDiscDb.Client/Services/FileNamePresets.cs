namespace TheDiscDb.Client.Services;

/// <summary>
/// A predefined filename template with a display name and target media type.
/// </summary>
public sealed record FileNamePreset(string Name, string Template, string MediaType);

/// <summary>
/// Provides predefined filename templates for common Plex and Jellyfin naming conventions.
/// </summary>
public static class FileNamePresets
{
    public static IReadOnlyList<FileNamePreset> Movie { get; } = new List<FileNamePreset>
    {
        new("Plex Movie", "{fulltitle} {{imdb-{imdbid}}}.mkv", "movie"),
        new("Plex Movie (Edition)", "{fulltitle} {{edition-{edition}}} {{imdb-{imdbid}}}.mkv", "movie"),
        new("Plex Movie Extra", "{episodename}-{extratype}.mkv", "movie"),
        new("Jellyfin Movie", "{fulltitle} [imdbid-{imdbid}].mkv", "movie"),
        new("Jellyfin Movie (Version)", "{fulltitle} [imdbid-{imdbid}] - {resolution}.mkv", "movie"),
        new("Jellyfin Movie Extra", "{episodename}-{extratype}.mkv", "movie"),
        new("Simple Movie", "{fulltitle} {resolution}.mkv", "movie"),
    };

    public static IReadOnlyList<FileNamePreset> Series { get; } = new List<FileNamePreset>
    {
        new("Plex Episode", "{title} ({year}) - s{seasonnumber}e{episodenumber} - {episodename}.mkv", "series"),
        new("Jellyfin Episode", "{title} ({year}) S{seasonnumber}E{episodenumber} {episodename}.mkv", "series"),
        new("Simple Episode", "{title} S{seasonnumber}E{episodenumber} {episodename}.mkv", "series"),
    };

    /// <summary>
    /// Returns the appropriate presets for the given media type.
    /// </summary>
    public static IReadOnlyList<FileNamePreset> GetPresetsForType(string? mediaType)
    {
        if (string.IsNullOrWhiteSpace(mediaType))
        {
            return [];
        }

        return mediaType.Equals("series", StringComparison.OrdinalIgnoreCase)
            ? Series
            : Movie;
    }
}
