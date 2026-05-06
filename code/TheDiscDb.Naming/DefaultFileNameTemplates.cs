using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace TheDiscDb.Naming;

/// <summary>
/// Built-in default file-name templates for each disc-item type.
/// Acts as the single source of truth shared by the server (validation /
/// fallback) and the Blazor client (rendering when the user has no override).
/// </summary>
public static class DefaultFileNameTemplates
{
    public const string MainMovie = "{fulltitle} [{resolution}].mkv";
    public const string Episode = "{title}.S{seasonnumber}.E{episodenumber}.{episodename}.mkv";
    public const string Extra = "{description}.mkv";
    public const string Trailer = "{description}.mkv";
    public const string DeletedScene = "{description}.mkv";
    public const string Other = "{description}.mkv";
    public const string Interview = "{description}.mkv";
    public const string Featurette = "{description}.mkv";
    public const string Scene = "{description}.mkv";
    public const string Music = "{description}.mkv";
    public const string Short = "{description}.mkv";

    private static readonly Dictionary<string, string> Map =
        new(StringComparer.OrdinalIgnoreCase)
        {
            [ItemTypeNames.MainMovie] = MainMovie,
            [ItemTypeNames.Episode] = Episode,
            [ItemTypeNames.Extra] = Extra,
            [ItemTypeNames.Other] = Other,
            [ItemTypeNames.Interview] = Interview,
            [ItemTypeNames.Featurette] = Featurette,
            [ItemTypeNames.Scene] = Scene,
            [ItemTypeNames.Music] = Music,
            [ItemTypeNames.Short] = Short,
            [ItemTypeNames.Trailer] = Trailer,
            [ItemTypeNames.DeletedScene] = DeletedScene,
        };

    public static IReadOnlyDictionary<string, string> All { get; } =
        new ReadOnlyDictionary<string, string>(Map);

    /// <summary>
    /// All item types that have a built-in default template. Order is stable
    /// and matches the order used in the customize UI.
    /// </summary>
    public static IReadOnlyList<string> KnownItemTypes { get; } = new[]
    {
        ItemTypeNames.MainMovie,
        ItemTypeNames.Episode,
        ItemTypeNames.Extra,
        ItemTypeNames.Other,
        ItemTypeNames.Interview,
        ItemTypeNames.Featurette,
        ItemTypeNames.Scene,
        ItemTypeNames.Music,
        ItemTypeNames.Short,
        ItemTypeNames.Trailer,
        ItemTypeNames.DeletedScene,
    };

    public static bool IsKnownItemType(string? itemType) =>
        !string.IsNullOrWhiteSpace(itemType) && Map.ContainsKey(itemType);

    public static string? GetDefault(string? itemType)
    {
        if (string.IsNullOrWhiteSpace(itemType))
        {
            return null;
        }

        return Map.TryGetValue(itemType, out var template) ? template : null;
    }
}
