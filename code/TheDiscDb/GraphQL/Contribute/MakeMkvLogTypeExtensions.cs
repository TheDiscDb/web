namespace TheDiscDb.GraphQL.Contribute;

// Disc scan evidence carried on the MakeMKV log model is used by the server's manifest mapping and
// comparisons only; keep it out of the contribution schema.
public class MakeMkvDiscInfoTypeExtension : ObjectTypeExtension<MakeMkv.DiscInfo>
{
    protected override void Configure(IObjectTypeDescriptor<MakeMkv.DiscInfo> descriptor)
    {
        descriptor.Ignore(info => info.SourceKind);
    }
}

public class MakeMkvSegmentTypeExtension : ObjectTypeExtension<MakeMkv.Segment>
{
    protected override void Configure(IObjectTypeDescriptor<MakeMkv.Segment> descriptor)
    {
        descriptor.Ignore(segment => segment.Category);
        descriptor.Ignore(segment => segment.SourceCodec);
        descriptor.Ignore(segment => segment.Pid);
        descriptor.Ignore(segment => segment.CodingTypeCode);
        descriptor.Ignore(segment => segment.FormatCode);
        descriptor.Ignore(segment => segment.RateCode);
    }
}
