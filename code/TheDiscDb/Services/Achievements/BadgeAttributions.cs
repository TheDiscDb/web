namespace TheDiscDb.Services.Achievements;

using System.Collections.Generic;

/// <summary>Attribution record for a badge icon, as required by its licence.</summary>
public sealed record BadgeAttribution(string Icon, string Author, string SourceUrl);

/// <summary>
/// Attribution data for the badge icons. All icons are from game-icons.net and licensed
/// under Creative Commons BY 3.0, which requires crediting the individual author. These are
/// surfaced on the public credits page.
/// </summary>
public static class BadgeAttributions
{
    public const string License = "Creative Commons BY 3.0";
    public const string LicenseUrl = "https://creativecommons.org/licenses/by/3.0/";
    public const string CollectionUrl = "https://game-icons.net/";

    public static IReadOnlyList<BadgeAttribution> All { get; } = new List<BadgeAttribution>
    {
        new("trophy", "Lorc", "https://game-icons.net/1x1/lorc/trophy.html"),
        new("medal", "Lorc", "https://game-icons.net/1x1/lorc/medal.html"),
        new("feather", "Lorc", "https://game-icons.net/1x1/lorc/feather.html"),
        new("tv", "Delapouite", "https://game-icons.net/1x1/delapouite/tv.html"),
        new("compact-disc", "Delapouite", "https://game-icons.net/1x1/delapouite/compact-disc.html"),
        new("magnifying-glass", "Lorc", "https://game-icons.net/1x1/lorc/magnifying-glass.html"),
        new("hourglass", "Lorc", "https://game-icons.net/1x1/lorc/hourglass.html"),
        new("calendar", "Delapouite", "https://game-icons.net/1x1/delapouite/calendar.html"),
        new("cardboard-box", "Delapouite", "https://game-icons.net/1x1/delapouite/cardboard-box.html"),
        new("price-tag", "Delapouite", "https://game-icons.net/1x1/delapouite/price-tag.html"),
        new("round-star", "Delapouite", "https://game-icons.net/1x1/delapouite/round-star.html"),
        new("campfire", "Lorc", "https://game-icons.net/1x1/lorc/campfire.html"),
        new("return-arrow", "Lorc", "https://game-icons.net/1x1/lorc/return-arrow.html"),
        new("on-target", "Lorc", "https://game-icons.net/1x1/lorc/on-target.html"),
    };
}
