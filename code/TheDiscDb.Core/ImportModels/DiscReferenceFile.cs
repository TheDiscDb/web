namespace TheDiscDb.ImportModels;

using System.Text.Json.Serialization;

public class DiscReferenceFile
{
    [JsonPropertyName("releasePath")]
    public string ReleasePath { get; set; } = string.Empty;

    [JsonPropertyName("disc")]
    public string Disc { get; set; } = string.Empty;
}
