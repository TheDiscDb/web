using TheDiscDb.OpticalDiscParsers.Bdmv.Models;

namespace TheDiscDb.OpticalDiscManifest.Generation;

internal static class BluRayPlaylistRules
{
    internal const int InfiniteStillMode = 2;

    /// <summary>
    /// Ends the playlist at the first play item that repeats the play item immediately
    /// before it: same clip, same in-time and same out-time. Discs loop background video
    /// this way, and MakeMKV lists the playlist only up to the loop: Spartacus'
    /// <c>00020.mpls</c> plays clip 6 then clip 10 122 times and MakeMKV reports segments
    /// <c>6,10</c> lasting 2:01; 1917's <c>00149.mpls</c> plays clip 174 then clip 175 251
    /// times and MakeMKV reports <c>174,175</c>. Play items after the loop are not listed
    /// either: Monsters University (Blu-ray bonus disc) <c>00000.mpls</c> plays clip 2 103
    /// times then the still clip 1, and MakeMKV lists only <c>00002.m2ts</c> and
    /// <c>00001.m2ts</c>, so the playlist is clip 2 alone. A still frame held indefinitely
    /// is shown once too, so an infinite still repeating an earlier infinite still is
    /// dropped wherever it occurs: Monsters University (UHD) <c>00002.mpls</c> plays stills
    /// 150, 151, 151, 151, 150 and MakeMKV reports <c>150,151</c>. Other non-adjacent
    /// repeats are kept, as MakeMKV keeps Stranger Things' <c>12,0,14,12,1,14</c>. Marks on
    /// dropped play items are dropped too. Stereoscopic playlists are left alone, because
    /// their pairing is declared per play item.
    /// </summary>
    internal static MplsPlaylist CollapseRepeatedPlayItems(MplsPlaylist playlist)
    {
        var items = playlist.PlayItems.OrderBy(item => item.Index).ToArray();
        if (items.Length < 2 || playlist.StereoVideoRelationships.Count > 0)
        {
            return playlist;
        }

        static bool SamePlayback(MplsPlayItem item, MplsPlayItem other)
            => item.Clips.Count <= 1
                && other.Clips.Count <= 1
                && string.Equals(item.ClipId, other.ClipId, StringComparison.OrdinalIgnoreCase)
                && item.InTime == other.InTime
                && item.OutTime == other.OutTime;

        var kept = new List<MplsPlayItem>(items.Length) { items[0] };
        for (int index = 1; index < items.Length; index++)
        {
            var item = items[index];
            if (SamePlayback(item, kept[^1]))
            {
                break;
            }

            bool repeatsStill = item.StillMode == InfiniteStillMode
                && kept.Any(earlier => earlier.StillMode == InfiniteStillMode && SamePlayback(item, earlier));
            if (!repeatsStill)
            {
                kept.Add(item);
            }
        }

        if (kept.Count == items.Length)
        {
            return playlist;
        }

        var indexMap = kept
            .Select((item, newIndex) => (item.Index, newIndex))
            .ToDictionary(pair => pair.Index, pair => pair.newIndex);
        return playlist with
        {
            PlayItems = kept.Select((item, newIndex) => item with { Index = newIndex }).ToArray(),
            Marks = playlist.Marks
                .Where(mark => indexMap.ContainsKey(mark.PlayItemReference))
                .OrderBy(mark => mark.Index)
                .Select((mark, newIndex) => mark with
                {
                    Index = newIndex,
                    PlayItemReference = indexMap[mark.PlayItemReference],
                })
                .ToArray(),
        };
    }

    /// <summary>
    /// Drops trailing play items, each shorter than half a second, whose stream table is
    /// narrower than the first play item's: no category holds more streams and at least
    /// one holds fewer. MakeMKV does not carry such a tail into the title. On Super Mario
    /// Bros (3D Blu-ray) <c>01000.mpls</c> plays clip 0 (video and audio) then the 0.42 s
    /// clip 13 (video only), and MakeMKV reports segment map <c>0</c> with clip 0's size
    /// alone, and lists <c>00000.m2ts</c> as equal to it; <c>01003.mpls</c> plays clip 3
    /// then clip 13, and MakeMKV lists only <c>00003.m2ts</c>. A longer tail is kept: Hell
    /// on Wheels Season 1 Disc 1 <c>00009.mpls</c> ends with the 1.04 s, video-only clip
    /// 17, and MakeMKV reports segments <c>1,17</c>. Trailing play items with the same
    /// stream table (<c>01001.mpls</c>, clips 1 and 12, both video only) are kept too.
    /// Marks on dropped play items are dropped, and the first play item is always kept.
    /// Stereoscopic playlists are left alone, because their pairing is declared per play
    /// item.
    /// </summary>
    internal static MplsPlaylist TrimTrailingNarrowerPlayItems(MplsPlaylist playlist)
    {
        const uint MaximumTrimmedTicks = 45000 / 2;
        var items = playlist.PlayItems.OrderBy(item => item.Index).ToArray();
        if (items.Length < 2 || playlist.StereoVideoRelationships.Count > 0)
        {
            return playlist;
        }

        int[] first = CountStreams(items[0].StreamTable);
        int keep = items.Length;
        while (keep > 1)
        {
            var item = items[keep - 1];
            int[] counts = CountStreams(item.StreamTable);
            bool narrower = counts.Zip(first).All(pair => pair.First <= pair.Second)
                && counts.Sum() < first.Sum();
            // Half a second at NTSC rates spans 12/15/30 frames in 0.5005 seconds.
            // Allow integer-tick rounding, not an additional whole frame.
            bool ntscHalfSecond = item.StreamTable.VideoStreams is [{ RateCode: 1 or 4 or 7 }];
            uint maximumTicks = ntscHalfSecond ? 22_523u : MaximumTrimmedTicks - 1;
            if (!narrower || item.OutTime < item.InTime || item.OutTime - item.InTime > maximumTicks)
            {
                break;
            }

            keep--;
        }

        if (keep == items.Length)
        {
            return playlist;
        }

        return playlist with
        {
            PlayItems = items.Take(keep).ToArray(),
            Marks = playlist.Marks
                .Where(mark => mark.PlayItemReference < keep)
                .OrderBy(mark => mark.Index)
                .Select((mark, newIndex) => mark with { Index = newIndex })
                .ToArray(),
        };

        static int[] CountStreams(MplsStreamTable table) =>
        [
            table.VideoStreams.Count,
            table.AudioStreams.Count,
            table.PresentationGraphicsStreams.Count,
            table.InteractiveGraphicsStreams.Count,
            table.SecondaryAudioStreams.Count,
            table.SecondaryVideoStreams.Count,
            table.PictureInPicturePresentationGraphicsStreams.Count,
            table.DolbyVisionVideoStreams.Count,
        ];
    }

    /// <summary>
    /// Returns how much of the last play item's clip the playlist leaves unplayed after
    /// its out-time. MakeMKV reports such a title with the clip's full length, and
    /// places its last chapter accordingly: a playlist ending 4.4 s before its clip ends
    /// reads 1:33 in MakeMKV where the play item alone lasts 1:29. Only a single-angle
    /// last play item over a clip with one STC sequence is extended, because only then
    /// is the clip's presentation end on the same timeline as the out-time.
    /// </summary>
    internal static long GetTrailingClipTicks(
        MplsPlaylist playlist,
        IReadOnlyDictionary<string, ClpiFile> clpiByClip)
    {
        var last = playlist.PlayItems.OrderBy(item => item.Index).LastOrDefault();
        if (last is null
            || last.Clips.Count > 1
            || !clpiByClip.TryGetValue(last.ClipId, out var clpi)
            || clpi.PresentationSummary is not { StcSequenceCount: 1 } summary
            || last.OutTime < last.InTime
            || last.OutTime < summary.StartTime
            || summary.EndTime <= last.OutTime)
        {
            return 0;
        }

        return summary.EndTime - last.OutTime;
    }
}
