namespace TheDiscDb.Services.DiscLookup;

using System.Collections.Generic;

/// <summary>
/// Naming-focused view of a disc identified by its globally-stable Disc ID: the disc plus each of
/// its titles and the item (movie / series episode) each title maps to.
/// </summary>
public sealed record DiscLookupResult(
    string? GlobalDiscId,
    string? Format,
    string? ContentHash,
    DiscLookupMedia? Media,
    DiscLookupRelease? Release,
    DiscLookupDisc? Disc,
    IReadOnlyList<DiscLookupTitle> Titles);

public sealed record DiscLookupMedia(
    string? Title,
    string? FullTitle,
    int Year,
    string? Type,
    DiscLookupExternalIds ExternalIds);

public sealed record DiscLookupExternalIds(
    string? Tmdb,
    string? Imdb,
    string? Tvdb);

public sealed record DiscLookupRelease(
    string? Slug,
    string? Title,
    int Year,
    string? RegionCode,
    string? Locale,
    string? Upc);

public sealed record DiscLookupDisc(
    string? Slug,
    string? Name,
    int Index);

public sealed record DiscLookupTitle(
    int Index,
    string? SourceFile,
    string? SegmentMap,
    string? Duration,
    string? DisplaySize,
    long Size,
    string? FileName,
    string? Resolution,
    IReadOnlyList<DiscLookupChapter> Chapters,
    IReadOnlyList<DiscLookupTrack> Tracks,
    DiscLookupItem? Item);

public sealed record DiscLookupItem(
    string? Title,
    string? Type,
    string? Description,
    string? Season,
    string? Episode);

public sealed record DiscLookupChapter(
    int Index,
    string? Title);

public sealed record DiscLookupTrack(
    int Index,
    string? Name,
    string? Type,
    string? Resolution,
    string? AspectRatio,
    string? AudioType,
    string? LanguageCode,
    string? Language,
    string? Description);
