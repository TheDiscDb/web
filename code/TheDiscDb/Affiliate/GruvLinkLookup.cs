namespace TheDiscDb.Affiliate;

using Microsoft.EntityFrameworkCore;
using TheDiscDb.InputModels;
using TheDiscDb.Web.Data;

/// <summary>
/// Per-circuit (Blazor Server) / per-request (Blazor WebAssembly) batched lookup for
/// <see cref="ReleaseAffiliateLink"/> rows keyed by slug pair. Scoped service: in Blazor Server
/// the same instance lives for the lifetime of the user's circuit, so the in-memory cache is
/// effectively a per-session cache. List pages can call <see cref="PreloadAsync"/> to warm it
/// with one DB round-trip and avoid N+1 queries during render.
/// </summary>
/// <remarks>
/// <para>Why not use an EF navigation property on <see cref="Release"/>? Because
/// <see cref="Release.Id"/> is unstable across DB rebuilds from <c>data/</c>, the affiliate table
/// is joined by slug pair, not by Release primary key. EF navigation properties require a real
/// foreign key, so we use an explicit query instead and batch it through this scoped service.</para>
/// <para>Cache lifetime caveat: the cache grows for as long as the Blazor circuit is alive, but
/// growth is bounded by how many distinct slug pairs the user navigates. Negative entries (rows
/// that don't exist) are also cached, so if <c>gruv-match</c> populates new rows mid-session the
/// user must reload to see them.</para>
/// </remarks>
public interface IGruvLinkLookup
{
    /// <summary>
    /// Returns the gruv affiliate link for the given slug pair, or <c>null</c> if none exists.
    /// Backed by an in-memory cache keyed by the slug pair so repeated calls within a single
    /// request (e.g. inside a list-rendering loop) all share one DB round-trip per unique pair.
    /// </summary>
    /// <param name="mediaItemSlug">Slug of the parent MediaItem, when the Release belongs to one.</param>
    /// <param name="boxsetSlug">Slug of the parent Boxset, when the Release belongs to one.</param>
    /// <param name="releaseSlug">Slug of the Release itself.</param>
    Task<ReleaseAffiliateLink?> GetAsync(string? mediaItemSlug, string? boxsetSlug, string releaseSlug, CancellationToken ct);

    /// <summary>
    /// Eagerly preloads all gruv affiliate rows for a set of releases under a single parent
    /// (MediaItem OR Boxset). Optional optimization for list pages that know the full set of
    /// release slugs they're about to render. Subsequent <see cref="GetAsync"/> calls for those
    /// pairs are served from cache without further round-trips.
    /// </summary>
    Task PreloadAsync(string? mediaItemSlug, string? boxsetSlug, IReadOnlyCollection<string> releaseSlugs, CancellationToken ct);
}

internal sealed class GruvLinkLookup(IDbContextFactory<SqlServerDataContext> dbContextFactory) : IGruvLinkLookup
{
    private const string GruvProvider = "gruv";

    // Cache key: (parentSlug, releaseSlug). Either MediaItemSlug or BoxsetSlug component is
    // populated, never both. Null/empty release slugs short-circuit to null (no DB call).
    private readonly Dictionary<(string parent, string release), ReleaseAffiliateLink?> cache = new(StringTupleComparer.Instance);

    public async Task<ReleaseAffiliateLink?> GetAsync(string? mediaItemSlug, string? boxsetSlug, string releaseSlug, CancellationToken ct)
    {
        if (string.IsNullOrEmpty(releaseSlug))
        {
            return null;
        }

        var parent = NormalizeParent(mediaItemSlug, boxsetSlug);
        if (parent is null)
        {
            return null;
        }

        if (this.cache.TryGetValue((parent.Value.Slug, releaseSlug), out var cached))
        {
            return cached;
        }

        await using var db = await dbContextFactory.CreateDbContextAsync(ct);
        var row = await QueryOne(db, parent.Value, releaseSlug, ct);
        this.cache[(parent.Value.Slug, releaseSlug)] = row;
        return row;
    }

    public async Task PreloadAsync(string? mediaItemSlug, string? boxsetSlug, IReadOnlyCollection<string> releaseSlugs, CancellationToken ct)
    {
        if (releaseSlugs.Count == 0)
        {
            return;
        }

        var parent = NormalizeParent(mediaItemSlug, boxsetSlug);
        if (parent is null)
        {
            return;
        }

        var uncached = releaseSlugs
            .Where(s => !string.IsNullOrEmpty(s) && !this.cache.ContainsKey((parent.Value.Slug, s)))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (uncached.Count == 0)
        {
            return;
        }

        await using var db = await dbContextFactory.CreateDbContextAsync(ct);
        var rows = parent.Value.IsBoxset
            ? await db.ReleaseAffiliateLinks.AsNoTracking()
                .Where(x => x.Provider == GruvProvider && x.BoxsetSlug == parent.Value.Slug && uncached.Contains(x.ReleaseSlug))
                .ToListAsync(ct)
            : await db.ReleaseAffiliateLinks.AsNoTracking()
                .Where(x => x.Provider == GruvProvider && x.MediaItemSlug == parent.Value.Slug && uncached.Contains(x.ReleaseSlug))
                .ToListAsync(ct);

        var byReleaseSlug = rows.ToDictionary(r => r.ReleaseSlug, StringComparer.OrdinalIgnoreCase);
        foreach (var rs in uncached)
        {
            this.cache[(parent.Value.Slug, rs)] = byReleaseSlug.GetValueOrDefault(rs);
        }
    }

    private static async Task<ReleaseAffiliateLink?> QueryOne(SqlServerDataContext db, ParentSlug parent, string releaseSlug, CancellationToken ct)
    {
        return parent.IsBoxset
            ? await db.ReleaseAffiliateLinks.AsNoTracking()
                .FirstOrDefaultAsync(x => x.Provider == GruvProvider && x.BoxsetSlug == parent.Slug && x.ReleaseSlug == releaseSlug, ct)
            : await db.ReleaseAffiliateLinks.AsNoTracking()
                .FirstOrDefaultAsync(x => x.Provider == GruvProvider && x.MediaItemSlug == parent.Slug && x.ReleaseSlug == releaseSlug, ct);
    }

    private static ParentSlug? NormalizeParent(string? mediaItemSlug, string? boxsetSlug)
    {
        var hasMedia = !string.IsNullOrEmpty(mediaItemSlug);
        var hasBoxset = !string.IsNullOrEmpty(boxsetSlug);
        return (hasMedia, hasBoxset) switch
        {
            (true, false) => new ParentSlug(mediaItemSlug!, IsBoxset: false),
            (false, true) => new ParentSlug(boxsetSlug!, IsBoxset: true),
            _ => null,
        };
    }

    private readonly record struct ParentSlug(string Slug, bool IsBoxset);

    private sealed class StringTupleComparer : IEqualityComparer<(string parent, string release)>
    {
        public static readonly StringTupleComparer Instance = new();
        public bool Equals((string parent, string release) x, (string parent, string release) y)
            => StringComparer.OrdinalIgnoreCase.Equals(x.parent, y.parent)
            && StringComparer.OrdinalIgnoreCase.Equals(x.release, y.release);
        public int GetHashCode((string parent, string release) obj)
            => HashCode.Combine(
                StringComparer.OrdinalIgnoreCase.GetHashCode(obj.parent),
                StringComparer.OrdinalIgnoreCase.GetHashCode(obj.release));
    }
}
