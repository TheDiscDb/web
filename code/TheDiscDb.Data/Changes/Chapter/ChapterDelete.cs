namespace TheDiscDb.Data.Changes.Chapter;

using System;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using TheDiscDb.InputModels;
using TheDiscDb.Web.Data;
using EntityChapter = TheDiscDb.InputModels.Chapter;

/// <summary>
/// Payload for delete — only needs identity fields to locate the chapter.
/// No editable fields; the proposed value is irrelevant. The original snapshot
/// captures the state at the time the user clicked "delete" for conflict detection.
/// </summary>
public sealed record ChapterDeleteDetails(
    string? MediaItemSlug,
    string? BoxsetSlug,
    string ReleaseSlug,
    string DiscSlug,
    int TitleIndex,
    int ChapterIndex)
{
    public string TargetEntityKey =>
        $"{(this.MediaItemSlug ?? this.BoxsetSlug) ?? string.Empty}/{this.ReleaseSlug}/{this.DiscSlug}/{this.TitleIndex.ToString(System.Globalization.CultureInfo.InvariantCulture)}/c{this.ChapterIndex.ToString(System.Globalization.CultureInfo.InvariantCulture)}";
}

/// <summary>
/// Deletes an existing <see cref="EntityChapter"/> from its parent disc item.
/// Validation ensures the chapter still exists and hasn't been modified since
/// the user submitted the delete suggestion.
/// </summary>
public sealed class ChapterDelete : ChangeBase<ChapterDeleteDetails>
{
    public const string Key = "chapter.delete";

    public ChapterDelete(ChapterDeleteDetails proposed)
        : base(proposed)
    {
    }

    public ChapterDelete(ChapterDeleteDetails proposed, JsonSerializerOptions jsonOptions)
        : base(proposed, jsonOptions)
    {
    }

    public override string TypeKey => Key;

    public override string TargetEntityKey => this.Proposed.TargetEntityKey;

    protected override async Task<ChapterDeleteDetails?> LoadCurrentSnapshotAsync(
        SqlServerDataContext context,
        CancellationToken cancellationToken)
    {
        var resolved = await ResolveChapterAsync(context, this.Proposed, cancellationToken);
        return resolved is null
            ? null
            : new ChapterDeleteDetails(
                this.Proposed.MediaItemSlug,
                this.Proposed.BoxsetSlug,
                this.Proposed.ReleaseSlug,
                this.Proposed.DiscSlug,
                this.Proposed.TitleIndex,
                resolved.Chapter.Index);
    }

    protected override async Task ApplyCoreAsync(
        SqlServerDataContext context,
        IChangeApplyContext apply,
        ChapterDeleteDetails? original,
        CancellationToken cancellationToken)
    {
        var resolved = await ResolveChapterAsync(context, this.Proposed, cancellationToken)
            ?? throw new InvalidOperationException(
                $"Chapter '{this.Proposed.TargetEntityKey}' not found at apply time. Validate should have caught this.");

        resolved.Item.Chapters.Remove(resolved.Chapter);
    }

    protected override string MissingTargetMessage()
        => $"Chapter '{this.Proposed.TargetEntityKey}' no longer exists (may have already been deleted).";

    protected override string? DescribeDrift(ChapterDeleteDetails original, ChapterDeleteDetails current)
    {
        // Delete uses identity-only details. If the chapter exists, it matches.
        // The original snapshot (full ChapterDetails) is validated separately by base class.
        return null;
    }

    private static async Task<ResolvedChapterForDelete?> ResolveChapterAsync(
        SqlServerDataContext context,
        ChapterDeleteDetails details,
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
                .Include(r => r.Discs).ThenInclude(d => d.Disc!).ThenInclude(disc => disc.Titles).ThenInclude(t => t.Item).ThenInclude(i => i!.Chapters)
                .FirstOrDefaultAsync(
                    r => r.Slug == releaseSlug && r.MediaItem != null && r.MediaItem.Slug == ms,
                    cancellationToken);
        }
        else
        {
            var bs = details.BoxsetSlug;
            release = await context.Releases
                .Include(r => r.Discs).ThenInclude(d => d.Disc!).ThenInclude(disc => disc.Titles).ThenInclude(t => t.Item).ThenInclude(i => i!.Chapters)
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
        var chapter = title?.Item?.Chapters.FirstOrDefault(c => c.Index == details.ChapterIndex);

        return chapter is null || title?.Item is null
            ? null
            : new ResolvedChapterForDelete(chapter, title.Item);
    }
}

internal sealed record ResolvedChapterForDelete(EntityChapter Chapter, DiscItemReference Item);
