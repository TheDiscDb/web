namespace TheDiscDb.Data.Changes.Track;

using System;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using TheDiscDb.InputModels;
using TheDiscDb.Web.Data;
using EntityTrack = TheDiscDb.InputModels.Track;

/// <summary>
/// Updates the editable fields of an existing <see cref="EntityTrack"/>.
/// Identity is resolved via Disc → Title(Index) → Tracks(Index).
/// </summary>
public sealed class TrackFieldsUpdate : ChangeBase<TrackFieldsDetails>
{
    public const string Key = "track.fields.update";

    public TrackFieldsUpdate(TrackFieldsDetails proposed)
        : base(proposed)
    {
    }

    public TrackFieldsUpdate(TrackFieldsDetails proposed, JsonSerializerOptions jsonOptions)
        : base(proposed, jsonOptions)
    {
    }

    public override string TypeKey => Key;

    public override string TargetEntityKey => this.Proposed.TargetEntityKey;

    protected override async Task<TrackFieldsDetails?> LoadCurrentSnapshotAsync(
        SqlServerDataContext context,
        CancellationToken cancellationToken)
    {
        var resolved = await ResolveTrackWithParentAsync(context, this.Proposed, cancellationToken);
        if (resolved is null)
        {
            return null;
        }

        var (track, disc, title) = resolved;
        return SnapshotFrom(
            track,
            this.Proposed.MediaItemSlug,
            this.Proposed.BoxsetSlug,
            this.Proposed.ReleaseSlug,
            disc.Slug!,
            title.Index);
    }

    protected override async Task ApplyCoreAsync(
        SqlServerDataContext context,
        IChangeApplyContext apply,
        TrackFieldsDetails? original,
        CancellationToken cancellationToken)
    {
        var resolved = await ResolveTrackWithParentAsync(context, this.Proposed, cancellationToken)
            ?? throw new InvalidOperationException(
                $"Track '{this.Proposed.TargetEntityKey}' not found at apply time. Validate should have caught this.");
        var track = resolved.Track;

        // NOTE on Type changes: this change type uses snapshot-diff semantics
        // (only fields where snapshot != proposed are written). When the user
        // changes Type from Audio to Video, the form should also clear the
        // audio-specific fields (AudioType, LanguageCode, Language) in the
        // proposed payload so the diff catches them. We intentionally do NOT
        // auto-null type-discriminated fields here because the Change type's
        // contract is "write exactly what the user proposed, nothing more" —
        // automatic cleanup would silently mutate fields the user didn't touch.
        // Mutual-exclusion is enforced at the UI / submit-validation layer.
        SetIfChanged(original, original?.Name, this.Proposed.Name, v => track.Name = v);
        SetIfChanged(original, original?.Type, this.Proposed.Type, v => track.Type = v);
        SetIfChanged(original, original?.Resolution, this.Proposed.Resolution, v => track.Resolution = v);
        SetIfChanged(original, original?.AspectRatio, this.Proposed.AspectRatio, v => track.AspectRatio = v);
        SetIfChanged(original, original?.AudioType, this.Proposed.AudioType, v => track.AudioType = v);
        SetIfChanged(original, original?.LanguageCode, this.Proposed.LanguageCode, v => track.LanguageCode = v);
        SetIfChanged(original, original?.Language, this.Proposed.Language, v => track.Language = v);
        SetIfChanged(original, original?.Description, this.Proposed.Description, v => track.Description = v);
    }

    protected override string MissingTargetMessage()
        => $"Track '{this.Proposed.TargetEntityKey}' no longer exists.";

    protected override string? DescribeDrift(TrackFieldsDetails original, TrackFieldsDetails current)
    {
        if (original.ReleaseSlug != current.ReleaseSlug
            || original.MediaItemSlug != current.MediaItemSlug
            || original.BoxsetSlug != current.BoxsetSlug
            || original.DiscSlug != current.DiscSlug
            || original.TitleIndex != current.TitleIndex
            || original.TrackIndex != current.TrackIndex)
        {
            return $"Track identity changed: snapshot '{original.TargetEntityKey}' vs current '{current.TargetEntityKey}'.";
        }

        // Only report drift on fields this suggestion is actually proposing to change.
        var drifted = new StringBuilder();
        if (this.Proposed.Name != original.Name)
            AppendIfDifferent(drifted, nameof(original.Name), original.Name, current.Name);
        if (this.Proposed.Type != original.Type)
            AppendIfDifferent(drifted, nameof(original.Type), original.Type, current.Type);
        if (this.Proposed.Resolution != original.Resolution)
            AppendIfDifferent(drifted, nameof(original.Resolution), original.Resolution, current.Resolution);
        if (this.Proposed.AspectRatio != original.AspectRatio)
            AppendIfDifferent(drifted, nameof(original.AspectRatio), original.AspectRatio, current.AspectRatio);
        if (this.Proposed.AudioType != original.AudioType)
            AppendIfDifferent(drifted, nameof(original.AudioType), original.AudioType, current.AudioType);
        if (this.Proposed.LanguageCode != original.LanguageCode)
            AppendIfDifferent(drifted, nameof(original.LanguageCode), original.LanguageCode, current.LanguageCode);
        if (this.Proposed.Language != original.Language)
            AppendIfDifferent(drifted, nameof(original.Language), original.Language, current.Language);
        if (this.Proposed.Description != original.Description)
            AppendIfDifferent(drifted, nameof(original.Description), original.Description, current.Description);

        return drifted.Length == 0
            ? null
            : "Track has been modified since the suggestion was submitted: " + drifted.ToString().TrimEnd(',', ' ');
    }

    public static TrackFieldsDetails SnapshotFrom(
        EntityTrack track,
        string? mediaItemSlug,
        string? boxsetSlug,
        string releaseSlug,
        string discSlug,
        int titleIndex)
    {
        ArgumentNullException.ThrowIfNull(track);
        ArgumentException.ThrowIfNullOrEmpty(discSlug);
        return new TrackFieldsDetails(
            MediaItemSlug: mediaItemSlug,
            BoxsetSlug: boxsetSlug,
            ReleaseSlug: releaseSlug,
            DiscSlug: discSlug,
            TitleIndex: titleIndex,
            TrackIndex: track.Index,
            Name: track.Name,
            Type: track.Type,
            Resolution: track.Resolution,
            AspectRatio: track.AspectRatio,
            AudioType: track.AudioType,
            LanguageCode: track.LanguageCode,
            Language: track.Language,
            Description: track.Description);
    }

    internal static async Task<ResolvedTrack?> ResolveTrackWithParentAsync(
        SqlServerDataContext context,
        TrackFieldsDetails details,
        CancellationToken cancellationToken)
    {
        // Contract: exactly one parent slug must be populated and DiscSlug is required.
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
        if (disc is null)
        {
            return null;
        }

        var title = disc.Titles.FirstOrDefault(t => t.Index == details.TitleIndex);
        if (title is null)
        {
            return null;
        }

        var track = title.Tracks.FirstOrDefault(tr => tr.Index == details.TrackIndex);
        return track is null ? null : new ResolvedTrack(track, disc, title);
    }
}

internal sealed record ResolvedTrack(EntityTrack Track, ReleaseDisc Disc, Title Title);
