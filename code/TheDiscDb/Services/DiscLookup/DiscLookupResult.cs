namespace TheDiscDb.Services.DiscLookup;

using System.Collections.Generic;

/// <summary>
/// Naming-focused view of a disc identified by its globally-stable Disc ID: the disc plus each of
/// its titles and the item (movie / series episode) each title maps to.
/// </summary>
public sealed record DiscLookupResult(
    string GlobalDiscId,
    string? Format,
    string? ContentHash,
    IReadOnlyList<DiscLookupTitle> Titles);

public sealed record DiscLookupTitle(
    int Index,
    string? SourceFile,
    string? SegmentMap,
    string? Duration,
    string? DisplaySize,
    long Size,
    DiscLookupItem? Item);

public sealed record DiscLookupItem(
    string? Title,
    string? Type,
    string? Season,
    string? Episode);
