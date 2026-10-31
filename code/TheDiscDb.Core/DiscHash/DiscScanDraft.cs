namespace TheDiscDb.Core.DiscHash;

using System.Text;

public sealed record DiscScanDraft
{
    private const string ManualRoutePrefix = "manual-";

    // Hex keeps arbitrary test strings in one path segment and survives URL lowercasing.
    public static string EncodeHashForRoute(string hash)
        => hash.Length == 32 && hash.All(Uri.IsHexDigit)
            ? hash.ToLowerInvariant()
            : ManualRoutePrefix + Convert.ToHexString(Encoding.UTF8.GetBytes(hash)).ToLowerInvariant();

    public static bool TryDecodeHashFromRoute(string routeHash, out string hash)
    {
        hash = routeHash;
        if (!routeHash.StartsWith(ManualRoutePrefix, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        string encoded = routeHash[ManualRoutePrefix.Length..];
        if (encoded.Length % 2 != 0 || !encoded.All(Uri.IsHexDigit))
        {
            return false;
        }

        hash = Encoding.UTF8.GetString(Convert.FromHexString(encoded));
        return true;
    }

    public string DiscHash { get; init; } = string.Empty;
    public bool HasManualHashOverride { get; init; }
    public string Format { get; init; } = string.Empty;
    public string? GlobalDiscId { get; init; }
    public string? Fingerprint { get; init; }
    public List<FileHashInfo> HashFiles { get; init; } = [];
    public List<DiscFingerprintFile> FingerprintFiles { get; init; } = [];
    public string ManifestJson { get; init; } = string.Empty;
    public List<string> Warnings { get; init; } = [];
    public DateTimeOffset CreatedUtc { get; init; }
    public string? ContributionId { get; init; }
}
