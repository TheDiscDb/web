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
                .ThenInclude(d => d.Release!)
                .ThenInclude(r => r.MediaItem!)
                .ThenInclude(m => m.Externalids)
            .FirstOrDefaultAsync(t => t.Id == parent.Id, cancellationToken);

        if (loaded?.Disc?.Release?.MediaItem is null)
        {
            return string.Empty;
        }

        var ctx = NamingContext.Create(loaded.Disc.Release.MediaItem, loaded.Disc.Release, loaded.Disc, loaded);
        var itemType = loaded.Item?.Type;

        return resolver.Format(itemType, ctx);
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
