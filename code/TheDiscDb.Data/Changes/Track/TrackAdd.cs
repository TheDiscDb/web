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
/// Adds a new <see cref="EntityTrack"/> to an existing title.
/// The <see cref="TrackFieldsDetails.TrackIndex"/> specifies the index for the
/// new track. Validation ensures no track with that index already exists.
/// </summary>
public sealed class TrackAdd : ChangeBase<TrackFieldsDetails>
{
    public const string Key = "track.add";

    public TrackAdd(TrackFieldsDetails proposed)
        : base(proposed)
    {
    }

    public TrackAdd(TrackFieldsDetails proposed, JsonSerializerOptions jsonOptions)
        : base(proposed, jsonOptions)
    {
    }

    public override string TypeKey => Key;

    public override string TargetEntityKey => this.Proposed.TargetEntityKey;

    public override bool RequiresOriginalSnapshot => false;

    protected override async Task<TrackFieldsDetails?> LoadCurrentSnapshotAsync(
        SqlServerDataContext context,
        CancellationToken cancellationToken)
    {
        var parent = await ResolveParentTitleAsync(context, this.Proposed, cancellationToken);
        if (parent is null)
        {
            return null;
        }

        // Check for duplicate index
        var existing = parent.Title.Tracks.FirstOrDefault(t => t.Index == this.Proposed.TrackIndex);
        if (existing is not null)
        {
            return TrackFieldsUpdate.SnapshotFrom(
                existing,
                this.Proposed.MediaItemSlug,
                this.Proposed.BoxsetSlug,
                this.Proposed.ReleaseSlug,
                parent.Disc.Slug!,
                parent.Title.Index);
        }

        // Return sentinel: parent exists, slot is free
        return new TrackFieldsDetails(
            this.Proposed.MediaItemSlug,
            this.Proposed.BoxsetSlug,
            this.Proposed.ReleaseSlug,
            this.Proposed.DiscSlug,
            this.Proposed.TitleIndex,
            this.Proposed.TrackIndex,
            Name: null, Type: null, Resolution: null, AspectRatio: null,
            AudioType: null, LanguageCode: null, Language: null, Description: null);
    }

    protected override async Task ApplyCoreAsync(
        SqlServerDataContext context,
        IChangeApplyContext apply,
        TrackFieldsDetails? original,
        CancellationToken cancellationToken)
    {
        var parent = await ResolveParentTitleAsync(context, this.Proposed, cancellationToken)
            ?? throw new InvalidOperationException(
                $"Parent title for '{this.Proposed.TargetEntityKey}' not found at apply time.");

        if (parent.Title.Tracks.Any(t => t.Index == this.Proposed.TrackIndex))
        {
            throw new ChangeApplyConflictException(
                this.TypeKey,
                $"Track at index {this.Proposed.TrackIndex} already exists on '{this.Proposed.TargetEntityKey}'.");
        }

        var track = new EntityTrack
        {
            Index = this.Proposed.TrackIndex,
            Name = this.Proposed.Name,
            Type = this.Proposed.Type,
            Resolution = this.Proposed.Resolution,
            AspectRatio = this.Proposed.AspectRatio,
            AudioType = this.Proposed.AudioType,
            LanguageCode = this.Proposed.LanguageCode,
            Language = this.Proposed.Language,
            Description = this.Proposed.Description
        };

        parent.Title.Tracks.Add(track);
    }

    protected override string MissingTargetMessage()
        => $"Parent title for '{this.Proposed.TargetEntityKey}' no longer exists.";

    protected override string? DescribeDrift(TrackFieldsDetails original, TrackFieldsDetails current)
    {
        if (current.Name is not null && original.Name is null)
        {
            return $"Track at index {this.Proposed.TrackIndex} already exists.";
        }

        return null;
    }

    private static async Task<ResolvedParentTitle?> ResolveParentTitleAsync(
        SqlServerDataContext context,
        TrackFieldsDetails details,
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
                .Include(r => r.Discs).ThenInclude(d => d.Titles).ThenInclude(t => t.Tracks)
                .FirstOrDefaultAsync(
                    r => r.Slug == releaseSlug && r.MediaItem != null && r.MediaItem.Slug == ms,
                    cancellationToken);
        }
        else
        {
            var bs = details.BoxsetSlug;
            release = await context.Releases
                .Include(r => r.Discs).ThenInclude(d => d.Titles).ThenInclude(t => t.Tracks)
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

        return title is null ? null : new ResolvedParentTitle(title, disc!);
    }
}

internal sealed record ResolvedParentTitle(Title Title, ReleaseDisc Disc);
