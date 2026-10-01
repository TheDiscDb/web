using System.Text.Json;
using System.Text.Json.Serialization;

namespace TheDiscDb.Contributions.OpticalDiscManifest;

/// <summary>
/// A deserialized Optical Disc Manifest (ODM) v1 document.
/// </summary>
/// <remarks>
/// Only the subset of the specification TheDiscDb consumes today is modelled. Documents are
/// validated against the full schema by <see cref="OpticalDiscManifestValidator"/> before they are
/// deserialized, so properties omitted here are still rejected when they are malformed.
/// </remarks>
public sealed class OpticalDiscManifestDocument
{
    /// <summary>The only schema version this application understands.</summary>
    public const int SupportedSchemaVersion = 1;

    public static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public int SchemaVersion { get; set; }
    public OpticalDiscManifestProducer? Producer { get; set; }
    public DateTimeOffset? CapturedAt { get; set; }
    public OpticalDiscManifestDisc? Disc { get; set; }
}

public sealed class OpticalDiscManifestProducer
{
    public string? Name { get; set; }
    public string? Version { get; set; }
    public string? Uri { get; set; }
}

public sealed class OpticalDiscManifestDisc
{
    public string? Format { get; set; }
    public string? Name { get; set; }
    public IList<OpticalDiscManifestIdentifier> Identifiers { get; set; } = [];
    public IList<OpticalDiscManifestFile> Files { get; set; } = [];
    public IList<OpticalDiscManifestTitle> Titles { get; set; } = [];
}

public sealed class OpticalDiscManifestIdentifier
{
    /// <summary>The content hash TheDiscDb uses to recognize a pressing.</summary>
    public const string ContentHashKind = "thediscdb-content-hash";

    public const string AacsDiscIdKind = "aacs-disc-id";
    public const string DvdDiscIdKind = "dvd-disc-id";

    public string? Kind { get; set; }
    public string? Value { get; set; }
}

public sealed class OpticalDiscManifestFile
{
    public string? Path { get; set; }
    public long SizeBytes { get; set; }
    public DateTimeOffset? ModifiedAt { get; set; }
}

public sealed class OpticalDiscManifestTitle
{
    public OpticalDiscManifestTitleSource? Source { get; set; }
    public double? DurationSeconds { get; set; }
    public long? SizeBytes { get; set; }
    public string? DisplaySize { get; set; }
    public int? ChapterCount { get; set; }
    public IList<OpticalDiscManifestChapter> Chapters { get; set; } = [];
    public IList<OpticalDiscManifestSegment> Segments { get; set; } = [];
    public IList<OpticalDiscManifestStream> Streams { get; set; } = [];
}

public sealed class OpticalDiscManifestTitleSource
{
    public string? Path { get; set; }
    public int? Title { get; set; }
    public int? TitleSet { get; set; }
    public int? TitleSetTitle { get; set; }
    public int? Part { get; set; }
}

public sealed class OpticalDiscManifestChapter
{
    public double StartSeconds { get; set; }
    public double? DurationSeconds { get; set; }
}

public sealed class OpticalDiscManifestSegment
{
    public string? Clip { get; set; }
    public double? StartSeconds { get; set; }
    public double? DurationSeconds { get; set; }
    public int? Angle { get; set; }
    public int? Cell { get; set; }
}

public sealed class OpticalDiscManifestStream
{
    public const string VideoType = "video";
    public const string AudioType = "audio";
    public const string SubtitleType = "subtitle";

    public string? Type { get; set; }
    public string? Category { get; set; }
    public string? Codec { get; set; }
    public string? Language { get; set; }
    public string? LanguageName { get; set; }
    public string? AudioLayout { get; set; }
    public string? Resolution { get; set; }
    public string? AspectRatio { get; set; }
}
