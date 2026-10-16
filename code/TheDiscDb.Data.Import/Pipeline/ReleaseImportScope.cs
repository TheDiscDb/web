namespace TheDiscDb.Data.Import.Pipeline;

using System.Collections.Generic;

public sealed record ReleaseImportScope(string Directory, string ReleaseSlug, int MediaItemId)
{
    public HashSet<string> OriginalParentGroups { get; } = new(System.StringComparer.OrdinalIgnoreCase);
    public HashSet<int> ChangedTitleIds { get; } = new();
}
