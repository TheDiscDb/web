namespace TheDiscDb.Naming;

using System;
using System.Collections.Generic;

/// <summary>
/// Canonical string values used for the disc-item type ("MainMovie", etc.).
/// Mirrors the values produced by <c>SummaryFileParser</c> and stored on
/// <c>DiscItemReference.Type</c>.
/// </summary>
public static class ItemTypeNames
{
    public const string MainMovie = "MainMovie";
    public const string Episode = "Episode";
    public const string Extra = "Extra";
    public const string Trailer = "Trailer";
    public const string DeletedScene = "DeletedScene";

    // Extra sub-categories. These behave the same as Extra during disc
    // identification but allow contributors to label content more precisely.
    public const string Other = "Other";
    public const string Interview = "Interview";
    public const string Featurette = "Featurette";
    public const string Scene = "Scene";
    public const string Music = "Music";
    public const string Short = "Short";

    /// <summary>
    /// All type strings that should be treated as members of the "Extra" family.
    /// Includes <see cref="Extra"/> itself plus the sub-categories.
    /// </summary>
    public static IReadOnlyList<string> ExtraTypes =>
    [
        Extra,
        Other,
        Interview,
        Featurette,
        Scene,
        Music,
        Short
    ];

    public static IReadOnlyList<string> AllTypes => 
    [
        MainMovie,
        Episode,
        Trailer,
        DeletedScene,
        Extra,
        Interview,
        Featurette,
        Scene,
        Music,
        Short,
        Other
    ];

    private static readonly HashSet<string> ExtraTypeSet =
        new(ExtraTypes, StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Returns true when <paramref name="type"/> is <see cref="Extra"/> or any
    /// of its sub-categories. Use this instead of comparing directly to the
    /// literal "Extra" string when the intent is "any extra-family item".
    /// </summary>
    public static bool IsExtra(string? type) =>
        !string.IsNullOrEmpty(type) && ExtraTypeSet.Contains(type);
}
