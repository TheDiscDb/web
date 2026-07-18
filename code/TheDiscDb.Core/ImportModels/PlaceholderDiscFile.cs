namespace TheDiscDb.ImportModels;

using System.Text.Json.Serialization;

/// <summary>
/// Represents a <c>discNN.placeholder.json</c> file in the <c>/data</c> repo: a disc that is
/// known to belong to a release but has not yet been contributed (no logs, no summary, no
/// identified titles). Carries only enough to slot it into the release and describe what is
/// missing. Materializes into a placeholder <see cref="TheDiscDb.InputModels.Disc"/>
/// (<c>IsPlaceholder = true</c>).
/// </summary>
public class PlaceholderDiscFile
{
    /// <summary>Suffix identifying a placeholder disc file, e.g. <c>disc02.placeholder.json</c>.</summary>
    public const string Suffix = ".placeholder.json";

    [JsonPropertyName("index")]
    public int Index { get; set; }

    [JsonPropertyName("name")]
    public string? Name { get; set; }

    [JsonPropertyName("slug")]
    public string? Slug { get; set; }

    [JsonPropertyName("format")]
    public string? Format { get; set; }
}
