using TheDiscDb.Services.Contributions;
using TheDiscDb.Web.Data;

namespace TheDiscDb.GraphQL.Contribute.Mutations;

internal enum DiscLogSourcePreference
{
    LogFirst,
    ManifestFirst
}

internal static class DiscLogSourceSelector
{
    public static DiscLogSourcePreference Select(UserContributionDisc disc, DiscScanMode mode, bool hasLogs, bool hasManifest)
    {
        if (!hasManifest)
        {
            return DiscLogSourcePreference.LogFirst;
        }

        if (!hasLogs)
        {
            return DiscLogSourcePreference.ManifestFirst;
        }

        // Saved item rows reference titles by MakeMKV-style source fields. Until the chosen
        // source is persisted per disc, keep existing identified discs on the historical log-first
        // path so title matching cannot shift underneath saved work.
        if (disc.Items.Count > 0)
        {
            return DiscLogSourcePreference.LogFirst;
        }

        return mode == DiscScanMode.Default
            ? DiscLogSourcePreference.ManifestFirst
            : DiscLogSourcePreference.LogFirst;
    }
}
