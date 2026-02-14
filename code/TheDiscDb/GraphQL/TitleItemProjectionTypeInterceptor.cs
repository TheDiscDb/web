using HotChocolate.Configuration;
using HotChocolate.Resolvers;
using HotChocolate.Types.Descriptors.Definitions;
using Microsoft.EntityFrameworkCore;
using TheDiscDb.InputModels;
using TheDiscDb.Web.Data;

namespace TheDiscDb.Data.GraphQL;

/// <summary>
/// Fixes the computed properties (<c>Description</c>, <c>ItemType</c>,
/// <c>Season</c>, <c>Episode</c>) on <see cref="Title"/> which delegate
/// to the <see cref="DiscItem.Item"/> navigation property.
///
/// When HotChocolate's projection is active and the client does not explicitly
/// request the <c>item</c> field, the navigation is not loaded and the computed
/// properties return empty strings.
///
/// The fix has two parts:
/// <list type="number">
///   <item>Always project the <c>DiscItemReferenceId</c> foreign key (a scalar)
///         so the resolver can look up the related entity on demand.</item>
///   <item>Replace the default (property-read) resolvers for the four computed
///         fields with resolvers that load <see cref="DiscItemReference"/> from
///         the database when <see cref="DiscItem.Item"/> was not projected.</item>
/// </list>
/// </summary>
public class TitleItemProjectionTypeInterceptor : TypeInterceptor
{
    // --- Stage 1: runs early so the projection convention picks up the FK ---
    public override void OnAfterInitialize(
        ITypeDiscoveryContext discoveryContext,
        DefinitionBase definition)
    {
        if (definition is not ObjectTypeDefinition typeDef
            || typeDef.RuntimeType != typeof(Title))
            return;

        // Ensure the FK is always included in EF Core projections.
        var fkField = typeDef.Fields.FirstOrDefault(
            f => f.Member?.Name == "DiscItemReferenceId");

        if (fkField != null)
        {
            fkField.ContextData["IsProjectedKey"] = true;
        }
    }

    // --- Stage 2: replace the default resolvers for the computed fields ---
    public override void OnBeforeCompleteType(
        ITypeCompletionContext completionContext,
        DefinitionBase definition)
    {
        if (definition is not ObjectTypeDefinition typeDef
            || typeDef.RuntimeType != typeof(Title))
            return;

        SetItemResolver(typeDef, "description", i => i.Title ?? string.Empty);
        SetItemResolver(typeDef, "itemType",    i => i.Type ?? string.Empty);
        SetItemResolver(typeDef, "season",      i => i.Season ?? string.Empty);
        SetItemResolver(typeDef, "episode",     i => i.Episode ?? string.Empty);
    }

    private static void SetItemResolver(
        ObjectTypeDefinition typeDef,
        string fieldName,
        Func<DiscItemReference, string> getValue)
    {
        var field = typeDef.Fields.FirstOrDefault(f => f.Name == fieldName);
        if (field is null) return;

        // Clear the PureResolver that was compiled from the CLR property getter
        // during CompileResolvers(). HotChocolate prefers PureResolver (synchronous)
        // over Resolver (async) at runtime, so if we don't clear it our custom
        // Resolver below will never be called.
        field.PureResolver = null;

        field.Resolver = async context =>
        {
            var title = context.Parent<Title>();
            await EnsureItemLoadedAsync(title, context);
            return title.Item is not null ? getValue(title.Item) : string.Empty;
        };
    }

    /// <summary>
    /// Loads the <see cref="DiscItemReference"/> for the given title if it
    /// has not already been populated by projection.  The result is cached
    /// on <see cref="DiscItem.Item"/> so that the other computed-field
    /// resolvers on the same parent entity reuse it without extra queries.
    /// </summary>
    private static async ValueTask EnsureItemLoadedAsync(
        Title title,
        IResolverContext context)
    {
        if (title.Item is not null || !title.DiscItemReferenceId.HasValue)
            return;

        var dbFactory = context.Service<IDbContextFactory<SqlServerDataContext>>();
        await using var db = dbFactory.CreateDbContext();
        title.Item = await db.DiscItemReferences.FindAsync(
            [title.DiscItemReferenceId.Value],
            context.RequestAborted);
    }
}
