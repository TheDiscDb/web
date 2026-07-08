namespace TheDiscDb.InputModels;

using HotChocolate.Data.Filters;

public class ReleaseDiscFilterType : FilterInputType<ReleaseDisc>
{
    protected override void Configure(IFilterInputTypeDescriptor<ReleaseDisc> descriptor)
    {
        descriptor.BindFieldsExplicitly();
        descriptor.Field(d => d.Id);
        descriptor.Field(d => d.ReleaseId);
        descriptor.Field(d => d.Release);
        descriptor.Field(d => d.DiscId);
        descriptor.Field(d => d.Disc);
        descriptor.Field(d => d.Index);
        descriptor.Field(d => d.Slug);
        descriptor.Field(d => d.Name);
        descriptor.Field(d => d.Titles);
        descriptor.Field(d => d.Disc!.Format).Name("format");
        descriptor.Field(d => d.Disc!.ContentHash).Name("contentHash");
        descriptor.Field(d => d.Disc!.GlobalDiscId).Name("globalDiscId");
    }
}
