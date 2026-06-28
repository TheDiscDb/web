using HotChocolate;
using HotChocolate.Types;
using Microsoft.EntityFrameworkCore;
using TheDiscDb.InputModels;
using TheDiscDb.Naming;
using TheDiscDb.Web.Data;

namespace TheDiscDb.GraphQL;

/// <summary>
/// Adds a server-resolved <c>filename</c> field to <see cref="Title"/> on the
/// public GraphQL schema. The field is purely additive — existing clients that
/// do not select it receive identical responses.
/// </summary>
[ExtendObjectType(typeof(Title))]
public class TitleFileNameExtension
{
    /// <summary>
    /// Returns the formatted file name for this title.
    /// </summary>
    /// <param name="templates">
    /// Optional per-item-type template overrides. Any item type not present
    /// falls back to the built-in default in <see cref="DefaultFileNameTemplates"/>.
    /// Invalid templates or unknown item types raise a <see cref="GraphQLException"/>.
    /// </param>
    public async Task<string> GetFilename(
        [Parent] Title parent,
        FileNameTemplateInput[]? templates,
        IDbContextFactory<SqlServerDataContext> dbFactory,
        CancellationToken cancellationToken)
    {
        var resolver = BuildResolver(templates);

        await using var db = dbFactory.CreateDbContext();

        // Pull the full chain (item, tracks, disc, release, mediaItem with externalIds).
        // The ExtendObjectType resolver does not benefit from QuickGrid-style projection,
        // so we eagerly load the data needed by NamingContext.Create.
        var loaded = await db.Titles
            .AsNoTracking()
            .Include(t => t.Item)
            .Include(t => t.Tracks)
            .Include(t => t.Disc!)
                .ThenInclude(d => d.ReleaseDiscs)
                .ThenInclude(rd => rd.Release!)
                .ThenInclude(r => r.MediaItem!)
                .ThenInclude(m => m.Externalids)
            .Include(t => t.Disc!)
                .ThenInclude(d => d.ReleaseDiscs)
                .ThenInclude(rd => rd.Release!)
                .ThenInclude(r => r.Boxset!)
            .FirstOrDefaultAsync(t => t.Id == parent.Id, cancellationToken);

        var loadedDisc = loaded?.Disc;
        var loadedReleaseDisc = loadedDisc?.ReleaseDiscs
            .FirstOrDefault(rd => rd.Release?.MediaItem is not null || rd.Release?.Boxset is not null);
        var loadedRelease = loadedReleaseDisc?.Release;
        if (loaded is null || loadedDisc is null || loadedRelease is null)
        {
            return string.Empty;
        }

        NamingContext ctx;
        string? itemType;

        if (loadedRelease.MediaItem is not null)
        {
            ctx = NamingContext.Create(loadedRelease.MediaItem, loadedRelease, loadedDisc, loaded);
            itemType = loaded.Item?.Type;
        }
        else if (loadedRelease.Boxset is not null)
        {
            // Boxset releases have no MediaItem in the DB. Look up the source movie / 
            // series release that contributed this disc: the data importer creates a
            // parallel release on each member MediaItem with the same release slug
            // and the same disc slug as the boxset's copy.
            var sourceRelease = await FindSourceReleaseForBoxsetDiscAsync(
                db, loadedRelease.Slug, loadedReleaseDisc?.Slug, cancellationToken);

            if (sourceRelease?.MediaItem is not null)
            {
                ctx = NamingContext.Create(sourceRelease.MediaItem, sourceRelease, loadedDisc, loaded);
            }
            else
            {
                // Custom or orphaned boxset disc with no matching source release.
                // Fall back to boxset-only metadata so the filename still resolves.
                ctx = NamingContext.Create(loadedRelease.Boxset, loadedRelease, loadedDisc, loaded);
            }

            itemType = loaded.Item?.Type;
        }
        else
        {
            return string.Empty;
        }

        return resolver.Format(itemType, ctx);
    }

    /// <summary>
    /// Locates the source <see cref="Release"/> (with its <see cref="MediaItem"/> and
    /// external IDs eagerly loaded) for a disc that belongs to a boxset release.
    /// Boxset member discs are imported as parallel rows: the source movie or series
    /// has a release with the same slug as the boxset's release, containing a disc
    /// with the same slug. Returns <c>null</c> if no such source release exists.
    /// </summary>
    private static async Task<Release?> FindSourceReleaseForBoxsetDiscAsync(
        SqlServerDataContext db,
        string? releaseSlug,
        string? discSlug,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(releaseSlug) || string.IsNullOrEmpty(discSlug))
        {
            return null;
        }

        return await db.Releases
            .AsNoTracking()
            .Include(r => r.MediaItem!)
                .ThenInclude(m => m.Externalids)
            .Where(r => r.Slug == releaseSlug
                && r.MediaItem != null
                && r.Discs.Any(d => d.Slug == discSlug))
            .FirstOrDefaultAsync(cancellationToken);
    }

    private static FileNameTemplateResolver BuildResolver(FileNameTemplateInput[]? templates)
    {
        if (templates is null || templates.Length == 0)
        {
            return new FileNameTemplateResolver();
        }

        var overrides = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (var input in templates)
        {
            if (string.IsNullOrWhiteSpace(input.ItemType))
            {
                throw new GraphQLException("FileNameTemplateInput.itemType cannot be empty.");
            }

            if (!DefaultFileNameTemplates.IsKnownItemType(input.ItemType))
            {
                throw new GraphQLException($"Unknown item type '{input.ItemType}'.");
            }

            if (string.IsNullOrWhiteSpace(input.Template))
            {
                throw new GraphQLException(
                    $"Template for item type '{input.ItemType}' cannot be empty. Omit the entry to use the default.");
            }

            if (input.Template.Length > 512)
            {
                throw new GraphQLException(
                    $"Template for item type '{input.ItemType}' exceeds 512 characters.");
            }

            var parseResult = NamingTemplate.Parse(input.Template);
            if (!parseResult.IsSuccess)
            {
                var message = parseResult.Errors is { Count: > 0 }
                    ? string.Join("; ", parseResult.Errors.Select(e => e.Message))
                    : "Invalid template.";
                throw new GraphQLException(
                    $"Invalid template for item type '{input.ItemType}': {message}");
            }

            overrides[input.ItemType] = input.Template;
        }

        return new FileNameTemplateResolver(overrides);
    }
}
