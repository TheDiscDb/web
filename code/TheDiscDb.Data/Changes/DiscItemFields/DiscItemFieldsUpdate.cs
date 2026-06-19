namespace TheDiscDb.Data.Changes.DiscItemFields;

using System;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using TheDiscDb.InputModels;
using TheDiscDb.Web.Data;

/// <summary>
/// Updates the editable fields of an existing <see cref="Title"/> (a.k.a. "disc
/// item") and its linked <see cref="DiscItemReference"/>. Identity is the
/// natural-key composite (parent slug, release slug, disc slug-or-index,
/// title index). The linked DiscItemReference is created lazily on Apply when
/// HasItem transitions to true with any Item* field populated.
/// </summary>
public sealed class DiscItemFieldsUpdate : ChangeBase<DiscItemFieldsDetails>
{
    public const string Key = "disc-item.fields.update";

    public DiscItemFieldsUpdate(DiscItemFieldsDetails proposed)
        : base(proposed)
    {
    }

    public DiscItemFieldsUpdate(DiscItemFieldsDetails proposed, JsonSerializerOptions jsonOptions)
        : base(proposed, jsonOptions)
    {
    }

    public override string TypeKey => Key;

    public override string TargetEntityKey => this.Proposed.TargetEntityKey;

    protected override async Task<DiscItemFieldsDetails?> LoadCurrentSnapshotAsync(
        SqlServerDataContext context,
        CancellationToken cancellationToken)
    {
        var resolved = await ResolveTitleWithParentAsync(context, this.Proposed, cancellationToken);
        if (resolved is null)
        {
            return null;
        }

        var (title, disc) = resolved;
        return SnapshotFrom(title, this.Proposed.MediaItemSlug, this.Proposed.BoxsetSlug, this.Proposed.ReleaseSlug, disc.Slug!);
    }

    protected override async Task ApplyCoreAsync(
        SqlServerDataContext context,
        IChangeApplyContext apply,
        DiscItemFieldsDetails? original,
        CancellationToken cancellationToken)
    {
        var resolved = await ResolveTitleWithParentAsync(context, this.Proposed, cancellationToken)
            ?? throw new InvalidOperationException(
                $"DiscItem '{this.Proposed.TargetEntityKey}' not found at apply time. Validate should have caught this.");
        var title = resolved.Title;

        SetIfChanged(original, original?.Comment, this.Proposed.Comment, v => title.Comment = v);
        SetIfChanged(original, original?.SourceFile, this.Proposed.SourceFile, v => title.SourceFile = v);
        SetIfChanged(original, original?.SegmentMap, this.Proposed.SegmentMap, v => title.SegmentMap = v);
        SetIfChanged(original, original?.Duration, this.Proposed.Duration, v => title.Duration = v);

        // DiscItemReference: lazily materialise if proposed wants one but none
        // exists on the entity. We treat "all Item* null AND original.HasItem
        // was false" as no-op so we don't create empty references.
        var proposedHasAnyItemField =
            !string.IsNullOrEmpty(this.Proposed.ItemTitle)
            || !string.IsNullOrEmpty(this.Proposed.ItemType)
            || !string.IsNullOrEmpty(this.Proposed.ItemDescription)
            || !string.IsNullOrEmpty(this.Proposed.ItemSeason)
            || !string.IsNullOrEmpty(this.Proposed.ItemEpisode);

        if (title.Item is null)
        {
            if (proposedHasAnyItemField)
            {
                title.Item = new DiscItemReference
                {
                    Title = this.Proposed.ItemTitle,
                    Type = this.Proposed.ItemType,
                    Description = this.Proposed.ItemDescription,
                    Season = this.Proposed.ItemSeason,
                    Episode = this.Proposed.ItemEpisode,
                };
            }
            // else: nothing to attach, leave null.
        }
        else
        {
            var item = title.Item;
            SetIfChanged(original, original?.ItemTitle, this.Proposed.ItemTitle, v => item.Title = v);
            SetIfChanged(original, original?.ItemType, this.Proposed.ItemType, v => item.Type = v);
            SetIfChanged(original, original?.ItemDescription, this.Proposed.ItemDescription, v => item.Description = v);
            SetIfChanged(original, original?.ItemSeason, this.Proposed.ItemSeason, v => item.Season = v);
            SetIfChanged(original, original?.ItemEpisode, this.Proposed.ItemEpisode, v => item.Episode = v);
        }
    }

    protected override string MissingTargetMessage()
        => $"DiscItem '{this.Proposed.TargetEntityKey}' no longer exists.";

    protected override string? DescribeDrift(DiscItemFieldsDetails original, DiscItemFieldsDetails current)
    {
        if (original.ReleaseSlug != current.ReleaseSlug
            || original.MediaItemSlug != current.MediaItemSlug
            || original.BoxsetSlug != current.BoxsetSlug
            || original.DiscSlug != current.DiscSlug
            || original.TitleIndex != current.TitleIndex)
        {
            return $"DiscItem identity changed: snapshot '{original.TargetEntityKey}' vs current '{current.TargetEntityKey}'.";
        }

        var drifted = new StringBuilder();
        AppendIfDifferent(drifted, nameof(original.Comment), original.Comment, current.Comment);
        AppendIfDifferent(drifted, nameof(original.SourceFile), original.SourceFile, current.SourceFile);
        AppendIfDifferent(drifted, nameof(original.SegmentMap), original.SegmentMap, current.SegmentMap);
        AppendIfDifferent(drifted, nameof(original.Duration), original.Duration, current.Duration);
        AppendIfDifferent(drifted, nameof(original.HasItem), original.HasItem, current.HasItem);
        AppendIfDifferent(drifted, nameof(original.ItemTitle), original.ItemTitle, current.ItemTitle);
        AppendIfDifferent(drifted, nameof(original.ItemType), original.ItemType, current.ItemType);
        AppendIfDifferent(drifted, nameof(original.ItemDescription), original.ItemDescription, current.ItemDescription);
        AppendIfDifferent(drifted, nameof(original.ItemSeason), original.ItemSeason, current.ItemSeason);
        AppendIfDifferent(drifted, nameof(original.ItemEpisode), original.ItemEpisode, current.ItemEpisode);

        return drifted.Length == 0
            ? null
            : "DiscItem has been modified since the suggestion was submitted: " + drifted.ToString().TrimEnd(',', ' ');
    }

    public static DiscItemFieldsDetails SnapshotFrom(
        Title title,
        string? mediaItemSlug,
        string? boxsetSlug,
        string releaseSlug,
        string discSlug)
    {
        ArgumentNullException.ThrowIfNull(title);
        ArgumentException.ThrowIfNullOrEmpty(discSlug);
        return new DiscItemFieldsDetails(
            MediaItemSlug: mediaItemSlug,
            BoxsetSlug: boxsetSlug,
            ReleaseSlug: releaseSlug,
            DiscSlug: discSlug,
            TitleIndex: title.Index,
            Comment: title.Comment,
            SourceFile: title.SourceFile,
            SegmentMap: title.SegmentMap,
            Duration: title.Duration,
            HasItem: title.Item is not null,
            ItemTitle: title.Item?.Title,
            ItemType: title.Item?.Type,
            ItemDescription: title.Item?.Description,
            ItemSeason: title.Item?.Season,
            ItemEpisode: title.Item?.Episode);
    }

    internal static async Task<ResolvedTitle?> ResolveTitleWithParentAsync(
        SqlServerDataContext context,
        DiscItemFieldsDetails details,
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
                .Include(r => r.Discs).ThenInclude(d => d.Titles).ThenInclude(t => t.Item)
                .FirstOrDefaultAsync(
                    r => r.Slug == releaseSlug && r.MediaItem != null && r.MediaItem.Slug == ms,
                    cancellationToken);
        }
        else
        {
            var bs = details.BoxsetSlug;
            release = await context.Releases
                .Include(r => r.Discs).ThenInclude(d => d.Titles).ThenInclude(t => t.Item)
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
        return title is null ? null : new ResolvedTitle(title, disc);
    }
}

internal sealed record ResolvedTitle(Title Title, ReleaseDisc Disc);
