using TheDiscDb.OpticalDiscManifest.Models;
using TheDiscDb.OpticalDiscParsers.Bdmv.Models;

namespace TheDiscDb.OpticalDiscManifest.Generation;

internal static class ManifestStreamMapper
{
    internal static IReadOnlyList<ManifestStream> CreateBluRayStreams(
        MplsPlaylist playlist,
        IReadOnlyDictionary<string, ClpiFile> clpiByClip)
    {
        var candidates = new List<MplsStream>();
        foreach (var item in playlist.PlayItems.OrderBy(item => item.Index))
        {
            candidates.AddRange(item.StreamTable.VideoStreams);
            candidates.AddRange(item.StreamTable.AudioStreams);
            candidates.AddRange(item.StreamTable.PresentationGraphicsStreams);
            candidates.AddRange(item.StreamTable.InteractiveGraphicsStreams);
            candidates.AddRange(item.StreamTable.SecondaryAudioStreams);
            candidates.AddRange(item.StreamTable.SecondaryVideoStreams);
        }

        var streams = new List<ManifestStream>();
        foreach (var stream in candidates
            .DistinctBy(item => (item.Category, item.Pid, item.CodingTypeCode, item.LanguageCode))
            .OrderBy(item => item.Category, StringComparer.Ordinal)
            .ThenBy(item => item.Pid)
            .ThenBy(item => item.CodingTypeCode))
        {
            var clpi = FindClpiStream(playlist, clpiByClip, stream.Pid);
            streams.Add(ProjectStream(
                stream.Category,
                stream.Pid,
                clpi?.CodingType ?? stream.CodingType,
                clpi?.CodingTypeCode ?? stream.CodingTypeCode,
                clpi?.FormatCode ?? stream.FormatCode,
                clpi?.RateCode ?? stream.RateCode,
                clpi?.LanguageCode ?? stream.LanguageCode,
                clpi));
        }

        return streams;
    }

    private static ClpiProgramStream? FindClpiStream(
        MplsPlaylist playlist,
        IReadOnlyDictionary<string, ClpiFile> clpiByClip,
        ushort? pid)
    {
        if (pid is null)
        {
            return null;
        }

        foreach (var clipId in playlist.PlayItems
            .SelectMany(item => item.Clips.Count > 0
                ? item.Clips.Select(clip => clip.ClipId)
                : new[] { item.ClipId })
            .Distinct(StringComparer.Ordinal))
        {
            if (clpiByClip.TryGetValue(clipId, out var clpi))
            {
                var stream = clpi.Programs
                    .SelectMany(program => program.Streams)
                    .FirstOrDefault(item => item.Pid == pid);
                if (stream is not null)
                {
                    return stream;
                }
            }
        }

        return null;
    }

    /// <summary>
    /// Maps CLPI stream evidence (ProgramInfo streams plus any ExtensionData streams,
    /// such as the MVC dependent-view stream declared for 3D dependent clips) into
    /// deterministic <see cref="ManifestStream"/> records for a standalone clip
    /// candidate. This is CLPI-only evidence: it does not cross-reference any MPLS
    /// playlist stream table.
    /// </summary>
    internal static IReadOnlyList<ManifestStream> CreateClpiStreams(ClpiFile clpi)
    {
        var candidates = clpi.Programs.SelectMany(program => program.Streams)
            .Concat(clpi.ExtensionStreams);

        var streams = new List<ManifestStream>();
        foreach (var stream in candidates
            .DistinctBy(item => (item.Category, item.Pid, item.CodingTypeCode, item.LanguageCode))
            .OrderBy(item => item.Category, StringComparer.Ordinal)
            .ThenBy(item => item.Pid)
            .ThenBy(item => item.CodingTypeCode))
        {
            streams.Add(ProjectStream(
                stream.Category,
                stream.Pid,
                stream.CodingType,
                stream.CodingTypeCode,
                stream.FormatCode,
                stream.RateCode,
                stream.LanguageCode,
                stream));
        }

        return streams;
    }

    private static ManifestStream ProjectStream(
        string category,
        int? pid,
        string codec,
        int? codingTypeCode,
        int? formatCode,
        int? rateCode,
        string? language,
        ClpiProgramStream? clipEvidence)
        => new()
        {
            Type = MapStreamType(category),
            Category = MapStreamCategory(category),
            Pid = pid,
            Codec = codec,
            CodingTypeCode = codingTypeCode,
            FormatCode = formatCode,
            RateCode = rateCode,
            DynamicRangeTypeCode = clipEvidence?.DynamicRangeTypeCode,
            ColorSpaceCode = clipEvidence?.ColorSpaceCode,
            HdrPlusFlag = clipEvidence?.HdrPlusFlag,
            Language = NormalizeLanguage(language),
            Resolution = MapVideoResolution(formatCode),
            AspectRatio = MapAspectRatio(clipEvidence?.AspectCode),
            FrameRate = MapFrameRate(rateCode),
            IsInterlaced = MapIsInterlaced(formatCode),
            SampleRate = MapSampleRate(rateCode, category),
        };

    private static string MapStreamType(string category)
        => category switch
        {
            "Video" or "SecondaryVideo" or "DolbyVisionVideo" => "video",
            "Audio" or "SecondaryAudio" => "audio",
            "PresentationGraphics" or "PictureInPicturePresentationGraphics" or "Subpicture" => "subtitle",
            "InteractiveGraphics" => "menu",
            _ => "unknown",
        };

    private static string? MapStreamCategory(string category)
        => category switch
        {
            "Video" => "video",
            "SecondaryVideo" => "secondaryVideo",
            "Audio" => "audio",
            "SecondaryAudio" => "secondaryAudio",
            "PresentationGraphics" => "presentationGraphics",
            "InteractiveGraphics" => "interactiveGraphics",
            "DolbyVisionVideo" => "dolbyVisionVideo",
            "PictureInPicturePresentationGraphics" => "pictureInPicturePresentationGraphics",
            "Subpicture" => "subpicture",
            _ => null,
        };

    private static string? MapVideoResolution(int? formatCode)
        => formatCode switch
        {
            1 or 3 => "720x480",
            2 or 7 => "720x576",
            4 or 6 => "1920x1080",
            5 => "1280x720",
            8 => "3840x2160",
            _ => null,
        };

    private static string? MapAspectRatio(int? aspectCode)
        => aspectCode switch
        {
            2 => "4:3",
            3 => "16:9",
            _ => null,
        };

    private static double? MapFrameRate(int? rateCode)
        => rateCode switch
        {
            1 => 23.976,
            2 => 24,
            3 => 25,
            4 => 29.97,
            6 => 50,
            7 => 59.94,
            _ => null,
        };

    private static bool? MapIsInterlaced(int? formatCode)
        => formatCode switch
        {
            1 or 2 or 4 => true,
            3 or 5 or 6 or 7 or 8 => false,
            _ => null,
        };

    private static string? MapSampleRate(int? rateCode, string category)
    {
        if (category is not ("Audio" or "SecondaryAudio"))
        {
            return null;
        }

        return rateCode switch
        {
            1 => "48 kHz",
            2 => "96 kHz",
            3 => "192 kHz",
            _ => null,
        };
    }

    internal static string? NormalizeLanguage(string? language)
        => string.IsNullOrWhiteSpace(language) || language == "und"
            ? null
            : language.Trim().ToLowerInvariant();
}
