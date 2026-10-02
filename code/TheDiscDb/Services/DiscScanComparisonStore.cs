using System.Text.Json;
using System.Text.Json.Serialization;
using TheDiscDb.Contributions.OpticalDiscManifest;
using TheDiscDb.Data.Import;

namespace TheDiscDb.Services;

/// <summary>
/// One run of the MakeMKV log vs. disc scan comparison.
/// </summary>
public sealed class DiscScanComparisonRecord
{
    public DateTimeOffset ComparedAt { get; set; }

    public DiscLogManifestComparisonStatus Status { get; set; }

    public string? Format { get; set; }

    public string? ProducerName { get; set; }

    public string? ProducerVersion { get; set; }

    public string? MakeMkvVersion { get; set; }

    public string? UserAgent { get; set; }

    public int LogTitleCount { get; set; }

    public int ManifestTitleCount { get; set; }

    public int MatchedTitleCount { get; set; }

    public bool OrderMatches { get; set; }

    public DiscLogManifestDifferences Differences { get; set; } = new();
}

/// <summary>
/// The comparison history for a disc, stored as JSON next to its log and scan in blob storage.
/// </summary>
/// <remarks>
/// Comparisons only matter while the disc scan rollout is in progress, so they live in a
/// readable file instead of a database table that would have to be migrated away later.
/// </remarks>
public sealed class DiscScanComparisonFile
{
    public List<DiscScanComparisonRecord> Comparisons { get; set; } = [];
}

public sealed class DiscScanComparisonStore
{
    // Re-comparing after a scanner fix is the common reason to run again, so a short history is enough.
    public const int MaxHistory = 20;

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter() }
    };

    private readonly IStaticAssetStore assetStore;

    public DiscScanComparisonStore(IStaticAssetStore assetStore)
    {
        this.assetStore = assetStore;
    }

    public async Task<DiscScanComparisonFile?> ReadAsync(string encodedContributionId, string encodedDiscId, CancellationToken cancellationToken = default)
    {
        string path = ContributionDiscAssets.ComparisonPath(encodedContributionId, encodedDiscId);
        if (!await this.assetStore.Exists(path, cancellationToken))
        {
            return null;
        }

        var data = await this.assetStore.Download(path, cancellationToken);
        return Deserialize(data.ToString());
    }

    public async Task AppendAsync(string encodedContributionId, string encodedDiscId, DiscScanComparisonRecord record, CancellationToken cancellationToken = default)
    {
        var file = await this.ReadAsync(encodedContributionId, encodedDiscId, cancellationToken) ?? new DiscScanComparisonFile();
        file.Comparisons.Insert(0, record);
        if (file.Comparisons.Count > MaxHistory)
        {
            file.Comparisons.RemoveRange(MaxHistory, file.Comparisons.Count - MaxHistory);
        }

        using var stream = new MemoryStream(JsonSerializer.SerializeToUtf8Bytes(file, JsonOptions));
        await this.assetStore.Save(
            stream,
            ContributionDiscAssets.ComparisonPath(encodedContributionId, encodedDiscId),
            ContentTypes.JsonContentType,
            cancellationToken);
    }

    public static string Serialize(DiscScanComparisonFile file) => JsonSerializer.Serialize(file, JsonOptions);

    public static DiscScanComparisonFile? Deserialize(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize<DiscScanComparisonFile>(json, JsonOptions);
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
