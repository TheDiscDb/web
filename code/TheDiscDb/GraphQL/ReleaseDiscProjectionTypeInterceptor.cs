using HotChocolate.Configuration;
using HotChocolate.Resolvers;
using HotChocolate.Types.Descriptors.Definitions;
using Microsoft.EntityFrameworkCore;
using TheDiscDb.InputModels;
using TheDiscDb.Web.Data;

namespace TheDiscDb.Data.GraphQL;

public class ReleaseDiscProjectionTypeInterceptor : TypeInterceptor
{
    public override void OnAfterInitialize(
        ITypeDiscoveryContext discoveryContext,
        DefinitionBase definition)
    {
        if (definition is not ObjectTypeDefinition typeDef
            || typeDef.RuntimeType != typeof(ReleaseDisc))
        {
            return;
        }

        var discIdField = typeDef.Fields.FirstOrDefault(
            f => f.Member?.Name == "DiscId");

        if (discIdField != null)
        {
            discIdField.ContextData["IsProjectedKey"] = true;
        }
    }

    public override void OnBeforeCompleteType(
        ITypeCompletionContext completionContext,
        DefinitionBase definition)
    {
        if (definition is not ObjectTypeDefinition typeDef
            || typeDef.RuntimeType != typeof(ReleaseDisc))
        {
            return;
        }

        SetResolver(typeDef, "format", static d => d.Disc?.Format);
        SetResolver(typeDef, "contentHash", static d => d.Disc?.ContentHash);
        SetResolver(typeDef, "titles", static d => d.Disc?.Titles ?? Array.Empty<Title>());
    }

    private static void SetResolver<TValue>(
        ObjectTypeDefinition typeDef,
        string fieldName,
        Func<ReleaseDisc, TValue> getValue)
    {
        var field = typeDef.Fields.FirstOrDefault(f => f.Name == fieldName);
        if (field is null)
        {
            return;
        }

        field.PureResolver = null;
        field.Resolver = async context =>
        {
            var releaseDisc = context.Parent<ReleaseDisc>();
            await EnsureDiscLoadedAsync(releaseDisc, context);
            return getValue(releaseDisc);
        };
    }

    private static async ValueTask EnsureDiscLoadedAsync(
        ReleaseDisc releaseDisc,
        IResolverContext context)
    {
        if (releaseDisc.Disc != null || releaseDisc.DiscId <= 0)
        {
            return;
        }

        var dbFactory = context.Service<IDbContextFactory<SqlServerDataContext>>();
        await using var db = dbFactory.CreateDbContext();
        releaseDisc.Disc = await db.Discs
            .Include(d => d.Titles)
            .ThenInclude(t => t.Item)
            .ThenInclude(i => i.Chapters)
            .Include(d => d.Titles)
            .ThenInclude(t => t.Tracks)
            .FirstOrDefaultAsync(d => d.Id == releaseDisc.DiscId, context.RequestAborted);
    }
}
