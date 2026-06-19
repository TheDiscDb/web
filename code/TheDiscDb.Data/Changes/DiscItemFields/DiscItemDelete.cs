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
/// Payload for delete — only needs identity fields to locate the disc item.
/// </summary>
public sealed record DiscItemDeleteDetails(
    string? MediaItemSlug,
    string? BoxsetSlug,
    string ReleaseSlug,
    string DiscSlug,
    int TitleIndex)
{
    public string TargetEntityKey =>
        $"{(this.MediaItemSlug ?? this.BoxsetSlug) ?? string.Empty}/{this.ReleaseSlug}/{this.DiscSlug}/{this.TitleIndex.ToString(System.Globalization.CultureInfo.InvariantCulture)}";
}

/// <summary>
/// Deletes an existing <see cref="Title"/> (disc item) and its linked
/// <see cref="DiscItemReference"/> from a disc. Cascades to chapters and tracks
/// via EF Core's configured cascade delete.
/// </summary>
public sealed class DiscItemDelete : ChangeBase<DiscItemDeleteDetails>
{
    public const string Key = "disc-item.delete";

    public DiscItemDelete(DiscItemDeleteDetails proposed)
        : base(proposed)
    {
    }

    public DiscItemDelete(DiscItemDeleteDetails proposed, JsonSerializerOptions jsonOptions)
        : base(proposed, jsonOptions)
    {
    }

    public override string TypeKey => Key;

    public override string TargetEntityKey => this.Proposed.TargetEntityKey;

    protected override async Task<DiscItemDeleteDetails?> LoadCurrentSnapshotAsync(
        SqlServerDataContext context,
        CancellationToken cancellationToken)
    {
        var title = await ResolveTitleAsync(context, this.Proposed, cancellationToken);
        return title is null
            ? null
            : new DiscItemDeleteDetails(
                this.Proposed.MediaItemSlug,
                this.Proposed.BoxsetSlug,
                this.Proposed.ReleaseSlug,
                this.Proposed.DiscSlug,
                title.Index);
    }

    protected override async Task ApplyCoreAsync(
        SqlServerDataContext context,
        IChangeApplyContext apply,
        DiscItemDeleteDetails? original,
        CancellationToken cancellationToken)
    {
        var disc = await ResolveParentDiscAsync(context, this.Proposed, cancellationToken)
            ?? throw new InvalidOperationException(
                $"Disc for '{this.Proposed.TargetEntityKey}' not found at apply time.");

        var title = disc.Titles.FirstOrDefault(t => t.Index == this.Proposed.TitleIndex)
            ?? throw new InvalidOperationException(
                $"Title '{this.Proposed.TargetEntityKey}' not found at apply time.");

        disc.Titles.Remove(title);
    }

    protected override string MissingTargetMessage()
        => $"Disc item '{this.Proposed.TargetEntityKey}' no longer exists (may have already been deleted).";

    protected override string? DescribeDrift(DiscItemDeleteDetails original, DiscItemDeleteDetails current)
    {
        return null;
    }

    private static async Task<Title?> ResolveTitleAsync(
        SqlServerDataContext context,
        DiscItemDeleteDetails details,
        CancellationToken cancellationToken)
    {
        var disc = await ResolveParentDiscAsync(context, details, cancellationToken);
        return disc?.Titles.FirstOrDefault(t => t.Index == details.TitleIndex);
    }

    private static async Task<ReleaseDisc?> ResolveParentDiscAsync(
        SqlServerDataContext context,
        DiscItemDeleteDetails details,
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
                .Include(r => r.Discs).ThenInclude(d => d.Disc!).ThenInclude(disc => disc.Titles)
                .FirstOrDefaultAsync(
                    r => r.Slug == releaseSlug && r.MediaItem != null && r.MediaItem.Slug == ms,
                    cancellationToken);
        }
        else
        {
            var bs = details.BoxsetSlug;
            release = await context.Releases
                .Include(r => r.Discs).ThenInclude(d => d.Disc!).ThenInclude(disc => disc.Titles)
                .FirstOrDefaultAsync(
                    r => r.Slug == releaseSlug && r.Boxset != null && r.Boxset.Slug == bs,
                    cancellationToken);
        }

        return release?.Discs.FirstOrDefault(d => d.Slug == details.DiscSlug);
    }
}
