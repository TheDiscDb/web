using HotChocolate.Data.Filters;
using TheDiscDb.InputModels;

namespace TheDiscDb.Data.GraphQL;

public class DiscDbFilterConvention : FilterConvention
{
    protected override void Configure(IFilterConventionDescriptor descriptor)
    {
        descriptor.AddDefaults();
        descriptor.BindRuntimeType<ReleaseDisc, ReleaseDiscFilterType>();
    }
}
