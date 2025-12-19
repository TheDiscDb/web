using System.Diagnostics.CodeAnalysis;
using System.Linq.Expressions;
using HotChocolate.Configuration;
using HotChocolate.Data.Filters;
using HotChocolate.Data.Filters.Expressions;
using HotChocolate.Language;
using TheDiscDb.Services.Server;
using TheDiscDb.Web.Data;

namespace TheDiscDb.GraphQL;

public abstract class EncodedIdTypeExtension<T> : ObjectTypeExtension<T>
    where T : IHasId
{
    protected override void Configure(IObjectTypeDescriptor<T> descriptor)
    {
        // Ensure Id is always projected so it's available for encodedId resolution
        descriptor.Field(x => x.Id).IsProjected(true);

        descriptor.Field("encodedId")
            .Type<NonNullType<EncodedIdType>>()
            .Resolve(context =>
            {
                var encoder = context.Service<IdEncoder>();
                var parent = context.Parent<T>();
                var id = encoder.Encode(parent.Id);
                return id;
            });
    }
}

public class ContributionTypeExtension : EncodedIdTypeExtension<UserContribution>
{
}

public class ContributionDiscTypeExtension : EncodedIdTypeExtension<UserContributionDisc>
{
}

public class ContributionDiscItemTypeExtension : EncodedIdTypeExtension<UserContributionDiscItem>
{
}

public class UserContributionAudioTrackTypeExtension : EncodedIdTypeExtension<UserContributionAudioTrack>
{
}

public class UserContributionChapterTypeExtension : EncodedIdTypeExtension<UserContributionChapter>
{
}

public class UserContributionDiscHashItemTypeExtension : EncodedIdTypeExtension<UserContributionDiscHashItem>
{
}

public class EncodedIdFilterConvention : FilterConvention
{
    protected override void Configure(IFilterConventionDescriptor descriptor)
    {
        descriptor.AddDefaults();

        // Allow filtering by encodedId on these types
        descriptor.BindRuntimeType<UserContribution, EncodedIdFilterType<UserContribution>>();
        descriptor.BindRuntimeType<UserContributionDisc, EncodedIdFilterType<UserContributionDisc>>();
        descriptor.BindRuntimeType<UserContributionDiscItem, EncodedIdFilterType<UserContributionDiscItem>>();
        descriptor.BindRuntimeType<UserContributionAudioTrack, EncodedIdFilterType<UserContributionAudioTrack>>();
        descriptor.BindRuntimeType<UserContributionChapter, EncodedIdFilterType<UserContributionChapter>>();
        descriptor.BindRuntimeType<UserContributionDiscHashItem, EncodedIdFilterType<UserContributionDiscHashItem>>();

        // Register the custom provider with encodedId handlers
        descriptor.Provider<EncodedIdQueryableFilterProvider>();
    }
}

public class EncodedIdQueryableFilterProvider : QueryableFilterProvider
{
    protected override void Configure(IFilterProviderDescriptor<QueryableFilterContext> descriptor)
    {
        descriptor.AddDefaultFieldHandlers();
        descriptor.AddFieldHandler<EncodedIdEqualsHandler>();
        descriptor.AddFieldHandler<EncodedIdNotEqualsHandler>();
        // TODO: More operations needed?
    }
}

public class EncodedIdFilterType<TEntity> : FilterInputType<TEntity>
    where TEntity : IHasId
{
    protected override void Configure(IFilterInputTypeDescriptor<TEntity> descriptor)
    {
        descriptor.BindFieldsImplicitly();
        descriptor.Field(t => t.Id)
                  .Name("encodedId")
                  .Type<EncodedIdOperationFilterInputType>();
    }
}

public class EncodedIdOperationFilterInputType : StringOperationFilterInputType
{
    protected override void Configure(IFilterInputTypeDescriptor descriptor)
    {
        descriptor.Operation(DefaultFilterOperations.Equals).Type<EncodedIdFilterScalarType>();
        descriptor.Operation(DefaultFilterOperations.NotEquals).Type<EncodedIdFilterScalarType>();
    }
}

public class EncodedIdEqualsHandler : QueryableStringOperationHandler
{
    private readonly IdEncoder encoder;

    public EncodedIdEqualsHandler(IdEncoder encoder, InputParser inputParser)
        : base(inputParser) => this.encoder = encoder;

    protected override int Operation => DefaultFilterOperations.Equals;

    public override bool CanHandle(
        ITypeCompletionContext context,
        IFilterInputTypeDefinition typeDefinition,
        IFilterFieldDefinition fieldDefinition)
        => fieldDefinition.Name == "encodedId";

    public override bool TryHandleOperation(QueryableFilterContext context, IFilterOperationField field, ObjectFieldNode node, [NotNullWhen(true)] out Expression? result)
    {
        return base.TryHandleOperation(context, field, node, out result);
    }

    public override Expression HandleOperation(
        QueryableFilterContext context,
        IFilterOperationField field,
        IValueNode value,
        object? parsedValue)
    {
        var id = encoder.Decode((string)parsedValue!);
        return BuildCompareExpression(context, Expression.Equal, id);
    }

    internal static Expression BuildCompareExpression(
        QueryableFilterContext context,
        Func<Expression, Expression, BinaryExpression> comparer,
        int id)
    {
        var instance = context.GetInstance();
        var property = Expression.Property(instance, "Id");
        var constant = Expression.Constant(id);
        return comparer(property, constant);
    }
}

public class EncodedIdNotEqualsHandler : QueryableStringOperationHandler
{
    private readonly IdEncoder encoder;

    public EncodedIdNotEqualsHandler(IdEncoder encoder, InputParser inputParser)
        : base(inputParser) => this.encoder = encoder;

    protected override int Operation => DefaultFilterOperations.NotEquals;

    public override bool CanHandle(
        ITypeCompletionContext context,
        IFilterInputTypeDefinition typeDefinition,
        IFilterFieldDefinition fieldDefinition)
        => fieldDefinition.Name == "encodedId";

    public override Expression HandleOperation(
        QueryableFilterContext context,
        IFilterOperationField field,
        IValueNode value,
        object? parsedValue)
    {
        var id = encoder.Decode((string)parsedValue!);
        return EncodedIdEqualsHandler.BuildCompareExpression(context, Expression.NotEqual, id);
    }
}

public class EncodedIdFilterScalarType : ScalarType<int, StringValueNode>
{
    private readonly IdEncoder encoder;

    public EncodedIdFilterScalarType(IdEncoder encoder)
        : base("EncodedIdFilter") => this.encoder = encoder;

    public override IValueNode ParseResult(object? resultValue)
    {
        return ParseValue(resultValue);
    }

    protected override int ParseLiteral(StringValueNode valueSyntax)
        => encoder.Decode(valueSyntax.Value);

    protected override StringValueNode ParseValue(int runtimeValue)
        => new StringValueNode(encoder.Encode(runtimeValue));
}

public class EncodedIdType : ScalarType<string, IntValueNode>
{
    private readonly IdEncoder encoder;

    public EncodedIdType(IdEncoder encoder) : base("EncodedId")
    {
        this.encoder = encoder ?? throw new ArgumentNullException(nameof(encoder));
    }

    public override IValueNode ParseResult(object? resultValue)
    {
        return ParseValue(resultValue);
    }

    protected override string ParseLiteral(IntValueNode valueSyntax)
    {
        return this.encoder.Encode(valueSyntax.ToInt32());
    }

    protected override IntValueNode ParseValue(string runtimeValue)
    {
        var id = this.encoder.Decode(runtimeValue);
        return new IntValueNode(id);
    }
}
