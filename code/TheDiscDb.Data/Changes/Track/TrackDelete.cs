namespace TheDiscDb.Data.Changes.Track;

using System;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using TheDiscDb.InputModels;
using TheDiscDb.Web.Data;
using EntityTrack = TheDiscDb.InputModels.Track;

/// <summary>
/// Payload for delete — only needs identity fields to locate the track.
/// </summary>
public sealed record TrackDeleteDetails(
    string? MediaItemSlug,
    string? BoxsetSlug,
    string ReleaseSlug,
    string DiscSlug,
    int TitleIndex,
    int TrackIndex)
{
    public string TargetEntityKey =>
        $"{(this.MediaItemSlug ?? this.BoxsetSlug) ?? string.Empty}/{this.ReleaseSlug}/{this.DiscSlug}/{this.TitleIndex.ToString(System.Globalization.CultureInfo.InvariantCulture)}/t{this.TrackIndex.ToString(System.Globalization.CultureInfo.InvariantCulture)}";
}

/// <summary>
/// Deletes an existing <see cref="EntityTrack"/> from its parent title.
/// </summary>
public sealed class TrackDelete : ChangeBase<TrackDeleteDetails>
{
    public const string Key = "track.delete";

    public TrackDelete(TrackDeleteDetails proposed)
        : base(proposed)
    {
    }

    public TrackDelete(TrackDeleteDetails proposed, JsonSerializerOptions jsonOptions)
        : base(proposed, jsonOptions)
    {
    }

    public override string TypeKey => Key;

    public override string TargetEntityKey => this.Proposed.TargetEntityKey;

    protected override async Task<TrackDeleteDetails?> LoadCurrentSnapshotAsync(
        SqlServerDataContext context,
        CancellationToken cancellationToken)
    {
        var resolved = await ResolveTrackAsync(context, this.Proposed, cancellationToken);
        return resolved is null
            ? null
            : new TrackDeleteDetails(
                this.Proposed.MediaItemSlug,
                this.Proposed.BoxsetSlug,
                this.Proposed.ReleaseSlug,
                this.Proposed.DiscSlug,
                this.Proposed.TitleIndex,
                resolved.Track.Index);
    }

    protected override async Task ApplyCoreAsync(
        SqlServerDataContext context,
        IChangeApplyContext apply,
        TrackDeleteDetails? original,
        CancellationToken cancellationToken)
    {
        var resolved = await ResolveTrackAsync(context, this.Proposed, cancellationToken)
            ?? throw new InvalidOperationException(
                $"Track '{this.Proposed.TargetEntityKey}' not found at apply time.");

        resolved.Title.Tracks.Remove(resolved.Track);
    }

    protected override string MissingTargetMessage()
        => $"Track '{this.Proposed.TargetEntityKey}' no longer exists (may have already been deleted).";

    protected override string? DescribeDrift(TrackDeleteDetails original, TrackDeleteDetails current)
    {
        return null;
    }

    private static async Task<ResolvedTrackForDelete?> ResolveTrackAsync(
        SqlServerDataContext context,
        TrackDeleteDetails details,
        CancellationToken cancellationToken)
    {
        var hasMedia = !string.IsNullOrWhiteSpace(details.MediaItemSlug);
        var hasBoxset = !string.IsNullOrWhiteSpace(details.BoxsetSlug);
        if (hasMedia == hasBoxset || string.IsNullOrEmpty(details.DiscSlug))
        {
            return null;
        }

        var releaseSlug = details.ReleaseSlug;
        Release? release;

        if (hasMedia)
        {
            var ms = details.MediaItemSlug;
            release = await context.Releases
                .Include(r => r.Discs).ThenInclude(d => d.Disc!).ThenInclude(disc => disc.Titles).ThenInclude(t => t.Tracks)
                .FirstOrDefaultAsync(
                    r => r.Slug == releaseSlug && r.MediaItem != null && r.MediaItem.Slug == ms,
                    cancellationToken);
        }
        else
        {
            var bs = details.BoxsetSlug;
            release = await context.Releases
                .Include(r => r.Discs).ThenInclude(d => d.Disc!).ThenInclude(disc => disc.Titles).ThenInclude(t => t.Tracks)
                .FirstOrDefaultAsync(
                    r => r.Slug == releaseSlug && r.Boxset != null && r.Boxset.Slug == bs,
                    cancellationToken);
        }

        if (release is null)
        {
            return null;
        }

        var disc = release.Discs.FirstOrDefault(d => d.Slug == details.DiscSlug);
        var title = disc?.Titles.FirstOrDefault(t => t.Index == details.TitleIndex);
        var track = title?.Tracks.FirstOrDefault(t => t.Index == details.TrackIndex);

        return track is null || title is null
            ? null
            : new ResolvedTrackForDelete(track, title);
    }
}

internal sealed record ResolvedTrackForDelete(EntityTrack Track, Title Title);
