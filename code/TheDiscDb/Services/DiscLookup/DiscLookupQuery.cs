namespace TheDiscDb.Services.DiscLookup;

using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using TheDiscDb.Web.Data;

/// <summary>
/// Shared query for looking a disc up by its globally-stable Disc ID. Used by both the anonymous
/// REST endpoint and (indirectly) any other caller; the GraphQL surface exposes the entity graph
/// directly rather than this DTO.
/// </summary>
public static partial class DiscLookupQuery
{
    // AACS Disc ID = 40 hex (SHA-1); DVD DVDDiscID = 32 hex (MD5).
    [GeneratedRegex("^([0-9A-Fa-f]{32}|[0-9A-Fa-f]{40})$")]
    private static partial Regex DiscIdPattern();

    /// <summary>True when the string is a 32- or 40-character hex Disc ID.</summary>
    public static bool IsValidDiscId(string? globalDiscId)
        => !string.IsNullOrEmpty(globalDiscId) && DiscIdPattern().IsMatch(globalDiscId);

    /// <summary>
    /// Returns the disc (with its titles/item mapping) whose Disc ID equals <paramref name="globalDiscId"/>
    /// (case-insensitive), or <c>null</c> when no disc matches.
    /// </summary>
    public static async Task<DiscLookupResult?> LookupAsync(
        SqlServerDataContext database, string globalDiscId, CancellationToken cancellationToken = default)
    {
        var normalized = globalDiscId.ToUpperInvariant();

        var disc = await database.Discs
            .AsNoTracking()
            .Include(d => d.Titles)
                .ThenInclude(t => t.Item)
            .FirstOrDefaultAsync(d => d.GlobalDiscId == normalized, cancellationToken);

        if (disc is null)
        {
            return null;
        }

        var titles = disc.Titles
            .OrderBy(t => t.Index)
            .Select(t => new DiscLookupTitle(
                t.Index,
                t.SourceFile,
                t.SegmentMap,
                t.Duration,
                t.DisplaySize,
                t.Size,
                t.Item is null
                    ? null
                    : new DiscLookupItem(
                        t.Item.Title,
                        t.Item.Type,
                        NullIfEmpty(t.Item.Season),
                        NullIfEmpty(t.Item.Episode))))
            .ToList();

        return new DiscLookupResult(normalized, disc.Format, disc.ContentHash, titles);
    }

    private static string? NullIfEmpty(string? value) => string.IsNullOrEmpty(value) ? null : value;
}
