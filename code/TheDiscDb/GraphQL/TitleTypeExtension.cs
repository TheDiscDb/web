using HotChocolate.Types;
using TheDiscDb.InputModels;

namespace TheDiscDb.Data.GraphQL;

public class TitleTypeExtension : ObjectTypeExtension<Title>
{
    protected override void Configure(IObjectTypeDescriptor<Title> descriptor)
    {
        // The Item navigation property must always be projected so that the
        // computed properties Description, ItemType, Season, and Episode
        // (which delegate to Item) return correct values even when the
        // caller does not explicitly request the "item" field.
        descriptor.Field(t => t.Item).IsProjected(true);
    }
}
