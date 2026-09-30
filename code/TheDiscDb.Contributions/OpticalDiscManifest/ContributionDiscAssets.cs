namespace TheDiscDb.Contributions.OpticalDiscManifest;

/// <summary>
/// The blob paths used for the files a contributor uploads for a single disc.
/// </summary>
/// <remarks>
/// A disc can be described by either a MakeMKV log or an Optical Disc Manifest, so the upload
/// endpoint and the readers that hydrate the identify flow have to agree on where each lands.
/// </remarks>
public static class ContributionDiscAssets
{
    public static string LogsPath(string contributionId, string discId)
        => $"{contributionId}/{discId}-logs.txt";

    public static string ManifestPath(string contributionId, string discId)
        => $"{contributionId}/{discId}-manifest.odm.json";
}
