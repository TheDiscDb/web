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
///
/// This must run during <c>OnAfterInitialize</c> because the projection
/// convention reads <c>IsProjectedKey</c> in <c>OnAfterCompleteName</c>
/// to build the <c>AlwaysProjectedFieldsKey</c> list.
/// </summary>
public class TitleItemProjectionTypeInterceptor : TypeInterceptor
{
    public override void OnAfterInitialize(ITypeDiscoveryContext discoveryContext, DefinitionBase definition)
    {
        if (definition is ObjectTypeDefinition typeDef && typeDef.RuntimeType == typeof(Title))
        {
            var itemField = typeDef.Fields.FirstOrDefault(
                f => f.Name is "item" or "Item" || f.Member?.Name == "Item");
            if (itemField != null)
            {
                itemField.ContextData["IsProjectedKey"] = true;
            }
        }
    }
}
