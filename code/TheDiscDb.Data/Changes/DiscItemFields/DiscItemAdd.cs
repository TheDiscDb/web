namespace TheDiscDb.Data.Changes.DiscItemFields;

using System;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using TheDiscDb.InputModels;
using TheDiscDb.Web.Data;

/// <summary>
/// Adds a new <see cref="Title"/> (disc item) to an existing disc.
/// The <see cref="DiscItemFieldsDetails.TitleIndex"/> specifies the index for
/// the new title. Validation ensures no title with that index already exists.
/// </summary>
public sealed class DiscItemAdd : ChangeBase<DiscItemFieldsDetails>
{
    public const string Key = "disc-item.add";

    public DiscItemAdd(DiscItemFieldsDetails proposed)
        : base(proposed)
    {
    }

    public DiscItemAdd(DiscItemFieldsDetails proposed, JsonSerializerOptions jsonOptions)
        : base(proposed, jsonOptions)
    {
    }

    public override string TypeKey => Key;

    public override string TargetEntityKey => this.Proposed.TargetEntityKey;

    public override bool RequiresOriginalSnapshot => false;

    protected override async Task<DiscItemFieldsDetails?> LoadCurrentSnapshotAsync(
        SqlServerDataContext context,
        CancellationToken cancellationToken)
    {
        var disc = await ResolveParentDiscAsync(context, this.Proposed, cancellationToken);
        if (disc is null)
        {
            return null;
        }

        var existing = disc.Titles.FirstOrDefault(t => t.Index == this.Proposed.TitleIndex);
        if (existing is not null)
        {
            return DiscItemFieldsUpdate.SnapshotFrom(
                existing,
                this.Proposed.MediaItemSlug,
                this.Proposed.BoxsetSlug,
                this.Proposed.ReleaseSlug,
                disc.Slug!);
        }

        // Sentinel: parent exists, slot is free
        return new DiscItemFieldsDetails(
            this.Proposed.MediaItemSlug,
            this.Proposed.BoxsetSlug,
            this.Proposed.ReleaseSlug,
            this.Proposed.DiscSlug,
            this.Proposed.TitleIndex,
            Comment: null, SourceFile: null, SegmentMap: null, Duration: null,
            HasItem: false, ItemTitle: null, ItemType: null,
            ItemDescription: null, ItemSeason: null, ItemEpisode: null);
    }

    protected override async Task ApplyCoreAsync(
        SqlServerDataContext context,
        IChangeApplyContext apply,
        DiscItemFieldsDetails? original,
        CancellationToken cancellationToken)
    {
        var disc = await ResolveParentDiscAsync(context, this.Proposed, cancellationToken)
            ?? throw new InvalidOperationException(
                $"Parent disc for '{this.Proposed.TargetEntityKey}' not found at apply time.");

        if (disc.Titles.Any(t => t.Index == this.Proposed.TitleIndex))
        {
            throw new ChangeApplyConflictException(
                this.TypeKey,
                $"Title at index {this.Proposed.TitleIndex} already exists on '{this.Proposed.TargetEntityKey}'.");
        }

        var title = new Title
        {
            Index = this.Proposed.TitleIndex,
            Comment = this.Proposed.Comment,
            SourceFile = this.Proposed.SourceFile,
            SegmentMap = this.Proposed.SegmentMap,
            Duration = this.Proposed.Duration,
        };

        // Create linked DiscItemReference if any item fields are populated
        if (this.Proposed.HasItem || HasAnyItemField(this.Proposed))
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

        disc.Titles.Add(title);
    }

    protected override string MissingTargetMessage()
        => $"Parent disc for '{this.Proposed.TargetEntityKey}' no longer exists.";

    protected override string? DescribeDrift(DiscItemFieldsDetails original, DiscItemFieldsDetails current)
    {
        if (current.HasItem && !original.HasItem)
        {
            return $"Title at index {this.Proposed.TitleIndex} already exists.";
        }

        return null;
    }

    private static bool HasAnyItemField(DiscItemFieldsDetails d) =>
        d.ItemTitle is not null || d.ItemType is not null || d.ItemDescription is not null
        || d.ItemSeason is not null || d.ItemEpisode is not null;

    private static async Task<Disc?> ResolveParentDiscAsync(
        SqlServerDataContext context,
        DiscItemFieldsDetails details,
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

        return release?.Discs.FirstOrDefault(d => d.Slug == details.DiscSlug);
    }
}
