using HotChocolate.Configuration;
using HotChocolate.Types.Descriptors.Definitions;
using TheDiscDb.InputModels;

namespace TheDiscDb.Data.GraphQL;

/// <summary>
/// Ensures the <c>Item</c> navigation property on <see cref="Title"/> is always
/// projected by EF Core so that the computed properties (<c>Description</c>,
/// <c>ItemType</c>, <c>Season</c>, <c>Episode</c>) which delegate to
/// <see cref="DiscItem.Item"/> return correct values even when the caller does
/// not explicitly request the <c>item</c> field in the GraphQL query.
/// </summary>
public class TitleItemProjectionTypeInterceptor : TypeInterceptor
{
    public override void OnBeforeCompleteType(ITypeCompletionContext completionContext, DefinitionBase definition)
    {
        if (definition is ObjectTypeDefinition typeDef && typeDef.RuntimeType == typeof(Title))
        {
            var itemField = typeDef.Fields.FirstOrDefault(f => f.Name == "item");
            if (itemField != null)
            {
                itemField.ContextData["IsProjectedKey"] = true;
            }
        }
    }
}
