namespace TheDiscDb.Services.DiscLookup;

using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using TheDiscDb.InputModels;
using TheDiscDb.Naming;
using TheDiscDb.Web.Data;

/// <summary>
/// Shared query for looking a disc up by its globally-stable Disc ID. Used by both the anonymous
/// REST endpoint and (indirectly) any other caller; the GraphQL surface exposes the entity graph
/// directly rather than this DTO.
/// </summary>
public static partial class DiscLookupQuery
{
    [GeneratedRegex("^[0-9A-Fa-f]{32}$")]
    private static partial Regex DiscHashPattern();

    // AACS Disc ID = 40 hex (SHA-1); DVD DVDDiscID = 32 hex (MD5).
    [GeneratedRegex("^([0-9A-Fa-f]{32}|[0-9A-Fa-f]{40})$")]
    private static partial Regex DiscIdPattern();

    /// <summary>True when the string is a 32- or 40-character hex Disc ID.</summary>
    public static bool IsValidDiscId(string? globalDiscId)
        => !string.IsNullOrEmpty(globalDiscId) && DiscIdPattern().IsMatch(globalDiscId);

    /// <summary>True when the string is a 32-character hex MakeMKV content hash.</summary>
    public static bool IsValidDiscHash(string? discHash)
        => !string.IsNullOrEmpty(discHash) && DiscHashPattern().IsMatch(discHash);

    /// <summary>
    /// Returns <b>every</b> release-disc that carries the given Disc ID — its own stored value or,
    /// for release-discs of the same canonical disc that don't store an id, via the shared-pressing
    /// fallback. Ordered so the release-disc that stores the id comes first. Empty when none match.
    /// </summary>
    public static async Task<IReadOnlyList<DiscLookupResult>> LookupAllAsync(SqlServerDataContext database, string globalDiscId, CancellationToken cancellationToken = default)
    {
        var normalized = globalDiscId.ToUpperInvariant();

        // Hop 1: the canonical disc(s) this id identifies (unique index seek).
        var canonicalDiscIds = await database.ReleaseDiscs
            .AsNoTracking()
            .Where(rd => rd.GlobalDiscId == normalized)
            .Select(rd => rd.DiscId)
            .Distinct()
            .ToListAsync(cancellationToken);

        if (canonicalDiscIds.Count == 0)
        {
            return [];
        }

        // Hop 2: every release-disc of those canonical discs that stores this id or nothing
        // (FK index seek). A sibling storing a *different* id is a re-press — excluded.
        var releaseDiscs = await BaseReleaseDiscQuery(database)
            .Where(rd => canonicalDiscIds.Contains(rd.DiscId)
                && (rd.GlobalDiscId == normalized || rd.GlobalDiscId == null)
                && rd.Release != null && (rd.Release.MediaItem != null || rd.Release.Boxset != null))
            .OrderByDescending(rd => rd.GlobalDiscId != null) // the storing release-disc first
            .ThenBy(rd => rd.Id)
            .ToListAsync(cancellationToken);

        var results = new List<DiscLookupResult>(releaseDiscs.Count);
        foreach (var releaseDisc in releaseDiscs)
        {
            if (releaseDisc.Disc is not null)
            {
                results.Add(await BuildResultAsync(database, releaseDisc, normalized, cancellationToken));
            }
        }

        return results;
    }

    /// <summary>
    /// Returns the first release-disc carrying <paramref name="globalDiscId"/> (see
    /// <see cref="LookupAllAsync"/>), or <c>null</c> when none matches.
    /// </summary>
    public static async Task<DiscLookupResult?> LookupAsync(SqlServerDataContext database, string globalDiscId, CancellationToken cancellationToken = default)
    {
        var all = await LookupAllAsync(database, globalDiscId, cancellationToken);
        return all.Count == 0 ? null : all[0];
    }

    public static async Task<DiscLookupResult?> LookupByDiscHashAsync(SqlServerDataContext database, string discHash, CancellationToken cancellationToken = default)
    {
        var normalized = discHash.ToUpperInvariant();
        var releaseDisc = await BaseReleaseDiscQuery(database)
            .Where(rd => rd.Disc != null && rd.Disc.ContentHash == normalized)
            .Where(rd => rd.Release != null && (rd.Release.MediaItem != null || rd.Release.Boxset != null))
            .OrderBy(rd => rd.Id)
            .FirstOrDefaultAsync(cancellationToken);

        return releaseDisc?.Disc is null
            ? null
            : await BuildResultAsync(database, releaseDisc, releaseDisc.EffectiveGlobalDiscId(), cancellationToken);
    }

    private static IQueryable<ReleaseDisc> BaseReleaseDiscQuery(SqlServerDataContext database)
    {
        return database.ReleaseDiscs
            .AsNoTracking()
            .AsSplitQuery()
            .Include(rd => rd.Disc!)
                .ThenInclude(d => d.Titles)
                    .ThenInclude(t => t.Item)
                        .ThenInclude(i => i!.Chapters)
            .Include(rd => rd.Disc!)
                .ThenInclude(d => d.Titles)
                    .ThenInclude(t => t.Tracks)
            .Include(rd => rd.Release!)
                .ThenInclude(r => r.MediaItem!)
                    .ThenInclude(m => m.Externalids)
            .Include(rd => rd.Release!)
                .ThenInclude(r => r.Boxset);
    }

    private static async Task<DiscLookupResult> BuildResultAsync(
        SqlServerDataContext database,
        ReleaseDisc releaseDisc,
        string? effectiveGlobalDiscId,
        CancellationToken cancellationToken)
    {
        var disc = releaseDisc.Disc!;
        var release = releaseDisc.Release;

        disc.Slug = releaseDisc.Slug;
        disc.Name = releaseDisc.Name;
        disc.Index = releaseDisc.Index;

        var sourceRelease = release?.Boxset is null
            ? null
            : await FindSourceReleaseForBoxsetDiscAsync(
                database,
                release.Slug,
                releaseDisc.Slug,
                cancellationToken);

        var fileNameResolver = new FileNameTemplateResolver();
        var titles = disc.Titles
            .OrderBy(t => t.Index)
            .Select(t => CreateTitle(t, disc, release, sourceRelease, fileNameResolver))
            .ToList();
        var media = release?.MediaItem ?? sourceRelease?.MediaItem;

        return new DiscLookupResult(
            effectiveGlobalDiscId,
            disc.Format,
            disc.ContentHash,
            media is null
                ? null
                : new DiscLookupMedia(
                    media.Title,
                    media.FullTitle,
                    media.Year,
                    media.Type,
                    new DiscLookupExternalIds(
                        media.Externalids?.Tmdb,
                        media.Externalids?.Imdb,
                        media.Externalids?.Tvdb)),
            release is null
                ? null
                : new DiscLookupRelease(
                    release.Slug,
                    release.Title,
                    release.Year,
                    release.RegionCode,
                    release.Locale,
                    release.Upc),
            new DiscLookupDisc(
                releaseDisc.Slug,
                releaseDisc.Name,
                releaseDisc.Index),
            titles);
    }

    private static DiscLookupTitle CreateTitle(
        Title title,
        Disc disc,
        Release? release,
        Release? sourceRelease,
        FileNameTemplateResolver fileNameResolver)
    {
        var namingContext = CreateNamingContext(title, disc, release, sourceRelease);
        var fileName = namingContext is null
            ? null
            : fileNameResolver.Format(title.Item?.Type, namingContext);

        return new DiscLookupTitle(
            title.Index,
            title.SourceFile,
            title.SegmentMap,
            title.Duration,
            title.DisplaySize,
            title.Size,
            NullIfEmpty(fileName),
            namingContext?.Resolution,
            title.Item?.Chapters
                .OrderBy(c => c.Index)
                .Select(c => new DiscLookupChapter(c.Index, c.Title))
                .ToList()
                ?? [],
            title.Tracks
                .OrderBy(t => t.Index)
                .Select(t => new DiscLookupTrack(
                    t.Index,
                    t.Name,
                    t.Type,
                    t.Resolution,
                    t.AspectRatio,
                    t.AudioType,
                    t.LanguageCode,
                    t.Language,
                    t.Description))
                .ToList(),
            title.Item is null
                ? null
                : new DiscLookupItem(
                    title.Item.Title,
                    title.Item.Type,
                    title.Item.Description,
                    NullIfEmpty(title.Item.Season),
                    NullIfEmpty(title.Item.Episode)));
    }

    private static NamingContext? CreateNamingContext(
        Title title,
        Disc disc,
        Release? release,
        Release? sourceRelease)
    {
        if (release?.MediaItem is not null)
        {
            return NamingContext.Create(release.MediaItem, release, disc, title);
        }

        if (sourceRelease?.MediaItem is not null)
        {
            return NamingContext.Create(sourceRelease.MediaItem, sourceRelease, disc, title);
        }

        return release?.Boxset is null
            ? null
            : NamingContext.Create(release.Boxset, release, disc, title);
    }

    private static Task<Release?> FindSourceReleaseForBoxsetDiscAsync(
        SqlServerDataContext database,
        string? releaseSlug,
        string? discSlug,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(releaseSlug) || string.IsNullOrEmpty(discSlug))
        {
            return Task.FromResult<Release?>(null);
        }

        return database.Releases
            .AsNoTracking()
            .Include(r => r.MediaItem!)
                .ThenInclude(m => m.Externalids)
            .Where(r => r.Slug == releaseSlug
                && r.MediaItem != null
                && r.Discs.Any(d => d.Slug == discSlug))
            .OrderBy(r => r.Id)
            .FirstOrDefaultAsync(cancellationToken);
    }

    private static string? NullIfEmpty(string? value) => string.IsNullOrEmpty(value) ? null : value;
}
