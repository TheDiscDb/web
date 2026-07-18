namespace TheDiscDb.Services.DiscLookup;

using System.Linq.Expressions;
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
    /// Returns the disc (with its titles/item mapping) whose Disc ID equals <paramref name="globalDiscId"/>
    /// (case-insensitive), or <c>null</c> when no disc matches.
    /// </summary>
    public static async Task<DiscLookupResult?> LookupAsync(SqlServerDataContext database, string globalDiscId, CancellationToken cancellationToken = default)
    {
        var normalized = globalDiscId.ToUpperInvariant();
        var disc = await LoadDiscAsync(
            database,
            d => d.GlobalDiscId == normalized,
            cancellationToken);

        return disc is null
            ? null
            : await BuildResultAsync(database, disc, cancellationToken);
    }

    public static async Task<DiscLookupResult?> LookupByDiscHashAsync(SqlServerDataContext database, string discHash, CancellationToken cancellationToken = default)
    {
        var normalized = discHash.ToUpperInvariant();
        var disc = await LoadDiscAsync(
            database,
            d => d.ContentHash == normalized,
            cancellationToken);

        return disc is null
            ? null
            : await BuildResultAsync(database, disc, cancellationToken);
    }

    private static Task<Disc?> LoadDiscAsync(
        SqlServerDataContext database,
        Expression<Func<Disc, bool>> predicate,
        CancellationToken cancellationToken)
    {
        return database.Discs
            .AsNoTracking()
            .AsSplitQuery()
            .Include(d => d.Titles)
                .ThenInclude(t => t.Item)
                    .ThenInclude(i => i!.Chapters)
            .Include(d => d.Titles)
                .ThenInclude(t => t.Tracks)
            .Include(d => d.ReleaseDiscs)
                .ThenInclude(rd => rd.Release!)
                    .ThenInclude(r => r.MediaItem!)
                        .ThenInclude(m => m.Externalids)
            .Include(d => d.ReleaseDiscs)
                .ThenInclude(rd => rd.Release!)
                    .ThenInclude(r => r.Boxset)
            .Where(predicate)
            .OrderBy(d => d.Id)
            .FirstOrDefaultAsync(cancellationToken);
    }

    private static async Task<DiscLookupResult> BuildResultAsync(
        SqlServerDataContext database,
        Disc disc,
        CancellationToken cancellationToken)
    {
        var releaseDisc = disc.ReleaseDiscs
            .Where(rd => rd.Release?.MediaItem is not null || rd.Release?.Boxset is not null)
            .OrderBy(rd => rd.Id)
            .FirstOrDefault();
        var release = releaseDisc?.Release;

        disc.Slug = releaseDisc?.Slug;
        disc.Name = releaseDisc?.Name;
        disc.Index = releaseDisc?.Index ?? 0;

        var sourceRelease = release?.Boxset is null
            ? null
            : await FindSourceReleaseForBoxsetDiscAsync(
                database,
                release.Slug,
                releaseDisc?.Slug,
                cancellationToken);

        var fileNameResolver = new FileNameTemplateResolver();
        var titles = disc.Titles
            .OrderBy(t => t.Index)
            .Select(t => CreateTitle(t, disc, release, sourceRelease, fileNameResolver))
            .ToList();
        var media = release?.MediaItem ?? sourceRelease?.MediaItem;

        return new DiscLookupResult(
            disc.GlobalDiscId,
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
            releaseDisc is null
                ? null
                : new DiscLookupDisc(
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
