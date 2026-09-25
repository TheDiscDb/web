using System.Text.Json;
using System.Text.Json.Serialization;
using TheDiscDb.OpticalDiscManifest.Models;

namespace TheDiscDb.OpticalDiscManifest.Serialization;

public static class OpticalDiscManifestJson
{
    public static JsonSerializerOptions Options { get; } = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        TypeInfoResolver = OpticalDiscManifestJsonContext.Default,
        WriteIndented = true,
    };

    public static byte[] Serialize(OpticalDiscManifestDocument document)
        => JsonSerializer.SerializeToUtf8Bytes(document, Options);
}

[JsonSourceGenerationOptions(
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    WriteIndented = true)]
[JsonSerializable(typeof(OpticalDiscManifestDocument))]
internal partial class OpticalDiscManifestJsonContext : JsonSerializerContext;
