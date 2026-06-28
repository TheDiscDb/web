namespace TheDiscDb.Data.Changes;

using System.Collections.Generic;
using System.Linq;

public sealed class ChangeFactory : IChangeFactory
{
    private readonly Dictionary<string, IChangeBuilder> buildersByKey;

    public ChangeFactory(IEnumerable<IChangeBuilder> builders)
    {
        this.buildersByKey = new Dictionary<string, IChangeBuilder>(System.StringComparer.Ordinal);
        foreach (var builder in builders)
        {
            if (!this.buildersByKey.TryAdd(builder.TypeKey, builder))
            {
                throw new DuplicateChangeBuilderException(builder.TypeKey);
            }
        }
    }

    public IReadOnlyCollection<string> RegisteredTypeKeys => this.buildersByKey.Keys.ToArray();

    public IChange Create(string typeKey, string proposedJson)
    {
        if (!this.buildersByKey.TryGetValue(typeKey, out var builder))
        {
            throw new UnknownChangeTypeException(typeKey);
        }

        return builder.Build(proposedJson);
    }
}
