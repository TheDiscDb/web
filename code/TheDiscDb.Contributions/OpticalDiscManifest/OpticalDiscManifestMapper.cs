using System.Globalization;
using MakeMkv;

namespace TheDiscDb.Contributions.OpticalDiscManifest;

/// <summary>
/// Projects an Optical Disc Manifest into the <see cref="DiscInfo"/> shape the contribution
/// pipeline already understands.
/// </summary>
/// <remarks>
/// This is the inverse of the <c>convert-makemkv-logs</c> tool. Mapping into <see cref="DiscInfo"/>
/// rather than teaching every consumer about manifests lets a contributor upload a manifest instead
/// of a MakeMKV log without changing the identify flow. String values deliberately reproduce
/// MakeMKV's own vocabulary ("Blu-ray disc", "Video", "Subtitles") because downstream code compares
/// against those literals.
/// </remarks>
public static class OpticalDiscManifestMapper
{
    public static DiscInfo ToDiscInfo(OpticalDiscManifestDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);

        OpticalDiscManifestDisc disc = document.Disc
            ?? throw new ArgumentException("The manifest does not describe a disc.", nameof(document));

        bool isDvd = string.Equals(disc.Format, "dvd", StringComparison.OrdinalIgnoreCase);

        var info = new DiscInfo
        {
            Name = disc.Name,
            Type = MapFormat(disc.Format)
        };

        foreach (var file in disc.Files)
        {
            info.HashInfo.Add(new HashInfoLogLine
            {
                Index = info.HashInfo.Count,
                // MakeMKV records bare file names on HSH lines; the manifest stores disc-relative
                // paths, so strip the directory back off.
                Name = FileName(file.Path),
                Size = file.SizeBytes,
                CreationTime = file.ModifiedAt?.UtcDateTime ?? default
            });
        }

        int index = 0;
        foreach (var title in disc.Titles)
        {
            info.Titles.Add(MapTitle(title, index++, isDvd));
        }

        return info;
    }

    /// <summary>
    /// Gets the TheDiscDb content hash recorded in the manifest, if present.
    /// </summary>
    public static string? GetContentHash(OpticalDiscManifestDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);

        return document.Disc?.Identifiers
            .FirstOrDefault(identifier => string.Equals(
                identifier.Kind,
                OpticalDiscManifestIdentifier.ContentHashKind,
                StringComparison.OrdinalIgnoreCase))?
            .Value;
    }

    /// <summary>
    /// Gets the pressing's disc id (AACS on Blu-ray and UHD, or the DVD disc id), if present.
    /// </summary>
    public static string? GetGlobalDiscId(OpticalDiscManifestDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);

        return document.Disc?.Identifiers
            .FirstOrDefault(identifier =>
                string.Equals(identifier.Kind, OpticalDiscManifestIdentifier.AacsDiscIdKind, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(identifier.Kind, OpticalDiscManifestIdentifier.DvdDiscIdKind, StringComparison.OrdinalIgnoreCase))?
            .Value;
    }

    private static Title MapTitle(OpticalDiscManifestTitle source, int index, bool isDvd)
    {
        long size = source.SizeBytes ?? 0;

        var title = new Title
        {
            Index = index,
            Size = size,
            ChapterCount = source.ChapterCount ?? source.Chapters.Count,
            Length = FormatDuration(source.DurationSeconds),
            DisplaySize = source.DisplaySize ?? FormatDisplaySize(size),
            Playlist = MapPlaylist(source.Source, isDvd),
            SegmentMap = MapSegmentMap(source.Segments)
        };

        int streamIndex = 0;
        foreach (var stream in source.Streams)
        {
            title.Segments.Add(MapStream(stream, streamIndex++));
        }

        return title;
    }

    private static Segment MapStream(OpticalDiscManifestStream source, int index)
    {
        string type = MapStreamType(source.Type);

        var segment = new Segment
        {
            Index = index,
            Type = type,
            Name = source.Codec,
            Resolution = source.Resolution,
            AspectRatio = source.AspectRatio
        };

        // MakeMKV only reports language on audio and subtitle tracks, and only reports the audio
        // layout on audio tracks. Copying them onto every stream would invent data the original
        // logs never carried.
        if (type is "Audio" or "Subtitles")
        {
            segment.LanguageCode = source.Language;
            segment.Language = source.LanguageName;
        }

        if (type == "Audio")
        {
            segment.AudioType = source.AudioLayout;
        }

        return segment;
    }

    private static string MapStreamType(string? type) => type?.Trim().ToLowerInvariant() switch
    {
        OpticalDiscManifestStream.VideoType => "Video",
        OpticalDiscManifestStream.AudioType => "Audio",
        // MakeMKV pluralizes this one, and the identify flow matches on its spelling.
        OpticalDiscManifestStream.SubtitleType => "Subtitles",
        "menu" => "Menu",
        "data" => "Data",
        _ => "Unknown"
    };

    private static string MapFormat(string? format) => format?.Trim().ToLowerInvariant() switch
    {
        "dvd" => "DVD disc",
        // MakeMKV reports UHD pressings as "Blu-ray disc" too, so both map to the same label.
        "blu-ray" or "uhd-blu-ray" => "Blu-ray disc",
        _ => "Unknown"
    };

    private static string? MapPlaylist(OpticalDiscManifestTitleSource? source, bool isDvd)
    {
        if (source is null)
        {
            return null;
        }

        if (isDvd)
        {
            int? titleNumber = source.Title ?? source.TitleSetTitle;
            return titleNumber?.ToString(CultureInfo.InvariantCulture);
        }

        string? path = source.Path;
        if (string.IsNullOrWhiteSpace(path))
        {
            return null;
        }

        // MakeMKV records only the playlist file name, not the full BDMV path.
        return FileName(path);
    }

    private static string? FileName(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return null;
        }

        string normalized = path.Replace('\\', '/');
        int separator = normalized.LastIndexOf('/');
        string name = separator >= 0 ? normalized[(separator + 1)..] : normalized;

        return name.Length == 0 ? null : name;
    }

    private static string MapSegmentMap(IList<OpticalDiscManifestSegment> segments)
    {
        // MakeMKV writes the segment map as a comma separated list of clip numbers with leading
        // zeros removed, so "BDMV/STREAM/00250.m2ts" appears as "250". Matching that exactly keeps
        // manifest-sourced discs comparable with log-sourced ones.
        var clips = segments
            .Select(segment => NormalizeClipId(segment.Clip))
            .Where(clip => clip is not null)
            .ToList();

        return clips.Count == 0 ? string.Empty : string.Join(",", clips);
    }

    private static string? NormalizeClipId(string? clip)
    {
        string? name = FileName(clip);
        if (name is null)
        {
            return null;
        }

        int extension = name.LastIndexOf('.');
        if (extension > 0)
        {
            name = name[..extension];
        }

        string trimmed = name.TrimStart('0');

        // A clip identified entirely by zeros still refers to clip 0.
        return trimmed.Length == 0 ? (name.Length == 0 ? null : "0") : trimmed;
    }

    private static string? FormatDuration(double? durationSeconds)
    {
        if (durationSeconds is not { } seconds || seconds < 0)
        {
            return null;
        }

        // MakeMKV writes durations as h:mm:ss, which is what Title.LengthAsTimeSpan parses back.
        return TimeSpan.FromSeconds(Math.Round(seconds)).ToString(@"h\:mm\:ss", CultureInfo.InvariantCulture);
    }

    private static string? FormatDisplaySize(long sizeBytes)
    {
        if (sizeBytes <= 0)
        {
            return null;
        }

        const double Gigabyte = 1024d * 1024d * 1024d;
        const double Megabyte = 1024d * 1024d;

        return sizeBytes >= Gigabyte
            ? string.Create(CultureInfo.InvariantCulture, $"{sizeBytes / Gigabyte:0.0} GB")
            : string.Create(CultureInfo.InvariantCulture, $"{sizeBytes / Megabyte:0.0} MB");
    }
}
