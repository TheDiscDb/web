namespace TheDiscDb.ImportModels;

using System.Text.Json.Serialization;

public class DiscReferenceFile
{
    [JsonPropertyName("releasePath")]
    public string ReleasePath { get; set; } = string.Empty;

    [JsonPropertyName("disc")]
    public string Disc { get; set; } = string.Empty;

    /// <summary>
    /// This referencing release's own pressing Disc ID (AACS Disc ID / DVDDiscID). Optional and
    /// distinct from the referenced disc's id: the two releases share content but are different
    /// physical pressings. Null when unknown.
    /// </summary>
    [JsonPropertyName("globalDiscId")]
    public string? GlobalDiscId { get; set; }
}
