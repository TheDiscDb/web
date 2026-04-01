using System;
using System.Collections.Generic;

namespace TheDiscDb.Naming;

/// <summary>
/// Defines the set of known template tokens and maps them to NamingContext properties.
/// </summary>
internal static class TokenDefinitions
{
    private static readonly Dictionary<string, Func<NamingContext, string?>> Accessors =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["title"] = c => c.Title,
            ["year"] = c => c.Year,
            ["fulltitle"] = c => c.FullTitle,
            ["resolution"] = c => c.Resolution,
            ["format"] = c => c.Format,
            ["tmdbid"] = c => c.TmdbId,
            ["imdbid"] = c => c.ImdbId,
            ["tvdbid"] = c => c.TvdbId,
            ["edition"] = c => c.Edition,
            ["part"] = c => c.Part,
            ["extratype"] = c => c.ExtraType,
            ["seasonnumber"] = c => c.SeasonNumber,
            ["episodenumber"] = c => c.EpisodeNumber,
            ["episodename"] = c => c.EpisodeName,
            ["airdate"] = c => c.AirDate,
        };

    public static bool IsKnown(string tokenName) =>
        Accessors.ContainsKey(tokenName);

    public static Func<NamingContext, string?> GetAccessor(string tokenName) =>
        Accessors[tokenName];

    public static IEnumerable<string> KnownTokens => Accessors.Keys;
}
