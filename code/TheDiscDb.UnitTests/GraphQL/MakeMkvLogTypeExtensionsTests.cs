using HotChocolate;
using HotChocolate.Execution;
using HotChocolate.Types;
using Microsoft.Extensions.DependencyInjection;
using TheDiscDb.GraphQL.Contribute;

namespace TheDiscDb.UnitTests.GraphQL;

public class MakeMkvLogTypeExtensionsTests
{
    [Test]
    public async Task MakeMkvLogTypes_OmitServerOnlyScanFields()
    {
        var executor = await new ServiceCollection()
            .AddGraphQLServer()
            .AddQueryType<LogQuery>()
            .AddTypeExtension<MakeMkvDiscInfoTypeExtension>()
            .AddTypeExtension<MakeMkvSegmentTypeExtension>()
            .BuildRequestExecutorAsync();

        var discInfo = executor.Schema.GetType<ObjectType>("DiscInfo");
        var segment = executor.Schema.GetType<ObjectType>("Segment");

        await Assert.That(discInfo.Fields.ContainsField("sourceKind")).IsFalse();
        foreach (var field in new[] { "category", "sourceCodec", "pid", "codingTypeCode", "formatCode", "rateCode" })
        {
            await Assert.That(segment.Fields.ContainsField(field)).IsFalse();
        }

        await Assert.That(segment.Fields.ContainsField("languageCode")).IsTrue();
    }

    public class LogQuery
    {
        public MakeMkv.DiscInfo GetLog() => new();
    }
}
