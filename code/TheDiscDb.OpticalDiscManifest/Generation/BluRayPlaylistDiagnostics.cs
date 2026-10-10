using TheDiscDb.OpticalDiscManifest.Models;
using TheDiscDb.OpticalDiscParsers.Bdmv.Models;

namespace TheDiscDb.OpticalDiscManifest.Generation;

internal static class BluRayPlaylistDiagnostics
{
    internal static void AddAuthoredEvidence(
        string path,
        MplsPlaylist playlist,
        IReadOnlyDictionary<string, ClpiFile> clips,
        ICollection<ManifestDiagnostic> diagnostics)
    {
        foreach (var batch in playlist.PlayItems.OrderBy(item => item.Index).Chunk(16))
        {
            var evidence = batch.Select(item =>
            {
                var references = item.Clips.Count > 0
                    ? item.Clips.Select(clip => $"{clip.ClipId}@{clip.StcId}")
                    : [$"{item.ClipId}@{item.StcId}"];
                var selected = item.StreamTable.VideoStreams.Concat(item.StreamTable.AudioStreams)
                    .Select(stream => $"{stream.Category}/{stream.StreamTypeCode}/{stream.Pid}:{stream.CodingTypeCode}/{stream.FormatCode}/{stream.RateCode}");
                string clipEvidence = string.Join("; ", item.Clips.Select(clip => clip.ClipId)
                    .Prepend(item.ClipId).Distinct(StringComparer.OrdinalIgnoreCase).Select(clipId =>
                    {
                        if (!clips.TryGetValue(clipId, out var clip))
                        {
                            return $"{clipId}: CLPI unavailable";
                        }

                        string timing = clip.PresentationSummary is { } summary
                            ? $"start={summary.StartTime},end={summary.EndTime},duration={summary.DurationTicks45k},stcCount={summary.StcSequenceCount}"
                            : "timing unavailable";
                        var sequences = clip.AtcSequences.SelectMany(atc => atc.StcSequences.Select(stc =>
                            $"atc={atc.Index},offsetStcId={atc.OffsetStcId},stcIndex={stc.Index},start={stc.PresentationStartTime},end={stc.PresentationEndTime}"));
                        var formats = clip.Programs.Select(program =>
                            $"program={program.Index},packetStart={program.SourcePacketNumberProgramSequenceStart},streams=["
                            + string.Join(",", program.Streams.Select(stream =>
                                $"{stream.Pid}:{stream.CodingTypeCode}/{stream.FormatCode}/{stream.RateCode}")) + "]");
                        return $"{clipId}: {timing},STC=[{string.Join("; ", sequences)}],programStreams=[{string.Join("; ", formats)}]";
                    }));
                return $"item={item.Index},clips=[{string.Join(",", references)}],in={item.InTime},out={item.OutTime},connection={item.ConnectionCondition},still={item.StillMode},multiAngle={item.IsMultiAngle},STN=[{string.Join(",", selected)}],CLPI=[{clipEvidence}]";
            });
            diagnostics.Add(new ManifestDiagnostic
            {
                Severity = "info",
                Code = "ODM_BD_PLAYLIST_AUTHORED_EVIDENCE",
                Path = path,
                Message = $"Original playlist evidence before loop/tail/split rules; times are 45 kHz ticks, stream values are numeric codec/format/rate codes. PlaybackType={playlist.AppInfo.PlaybackTypeCode},stereoRelationships={playlist.StereoVideoRelationships.Count},subPaths={playlist.SubPaths.Count},extensionSubPaths={playlist.ExtensionSubPaths.Count}. {string.Join("; ", evidence)}",
            });
        }
    }
}
