namespace TheDiscDb.Data.Changes.Chapter;

using System;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using TheDiscDb.InputModels;
using TheDiscDb.Web.Data;
using EntityChapter = TheDiscDb.InputModels.Chapter;

/// <summary>
/// Updates the editable fields of an existing <see cref="EntityChapter"/>.
/// Identity is resolved via Disc → Title(Index) → Item → Chapters(Index).
/// </summary>
public sealed class ChapterUpdate : ChangeBase<ChapterDetails>
{
    public const string Key = "chapter.update";

    public ChapterUpdate(ChapterDetails proposed)
        : base(proposed)
    {
    }

    public ChapterUpdate(ChapterDetails proposed, JsonSerializerOptions jsonOptions)
        : base(proposed, jsonOptions)
    {
    }

    public override string TypeKey => Key;

    public override string TargetEntityKey => this.Proposed.TargetEntityKey;

    protected override async Task<ChapterDetails?> LoadCurrentSnapshotAsync(
        SqlServerDataContext context,
        CancellationToken cancellationToken)
    {
        var resolved = await ResolveChapterWithParentAsync(context, this.Proposed, cancellationToken);
        if (resolved is null)
        {
            return null;
        }

        var (chapter, disc, title) = resolved;
        return SnapshotFrom(
            chapter,
            this.Proposed.MediaItemSlug,
            this.Proposed.BoxsetSlug,
            this.Proposed.ReleaseSlug,
            disc.Slug!,
            title.Index);
    }

    protected override async Task ApplyCoreAsync(
        SqlServerDataContext context,
        IChangeApplyContext apply,
        ChapterDetails? original,
        CancellationToken cancellationToken)
    {
        var resolved = await ResolveChapterWithParentAsync(context, this.Proposed, cancellationToken)
            ?? throw new InvalidOperationException(
                $"Chapter '{this.Proposed.TargetEntityKey}' not found at apply time. Validate should have caught this.");
        var chapter = resolved.Chapter;

        SetIfChanged(original, original?.Title, this.Proposed.Title, v => chapter.Title = v);
    }

    protected override string MissingTargetMessage()
        => $"Chapter '{this.Proposed.TargetEntityKey}' no longer exists.";

    protected override string? DescribeDrift(ChapterDetails original, ChapterDetails current)
    {
        if (original.ReleaseSlug != current.ReleaseSlug
            || original.MediaItemSlug != current.MediaItemSlug
            || original.BoxsetSlug != current.BoxsetSlug
            || original.DiscSlug != current.DiscSlug
            || original.TitleIndex != current.TitleIndex
            || original.ChapterIndex != current.ChapterIndex)
        {
            return $"Chapter identity changed: snapshot '{original.TargetEntityKey}' vs current '{current.TargetEntityKey}'.";
        }

        var drifted = new StringBuilder();
        AppendIfDifferent(drifted, nameof(original.Title), original.Title, current.Title);

        return drifted.Length == 0
            ? null
            : "Chapter has been modified since the suggestion was submitted: " + drifted.ToString().TrimEnd(',', ' ');
    }

    public static ChapterDetails SnapshotFrom(
        EntityChapter chapter,
        string? mediaItemSlug,
        string? boxsetSlug,
        string releaseSlug,
        string discSlug,
        int titleIndex)
    {
        ArgumentNullException.ThrowIfNull(chapter);
        ArgumentException.ThrowIfNullOrEmpty(discSlug);
        return new ChapterDetails(
            MediaItemSlug: mediaItemSlug,
            BoxsetSlug: boxsetSlug,
            ReleaseSlug: releaseSlug,
            DiscSlug: discSlug,
            TitleIndex: titleIndex,
            ChapterIndex: chapter.Index,
            Title: chapter.Title);
    }

    internal static async Task<ResolvedChapter?> ResolveChapterWithParentAsync(
        SqlServerDataContext context,
        ChapterDetails details,
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
        if (disc is null)
        {
            return null;
        }

        var title = disc.Titles.FirstOrDefault(t => t.Index == details.TitleIndex);
        if (title is null)
        {
            return null;
        }

        var chapter = title.Item?.Chapters.FirstOrDefault(c => c.Index == details.ChapterIndex);
        return chapter is null ? null : new ResolvedChapter(chapter, disc, title);
    }
}

internal sealed record ResolvedChapter(EntityChapter Chapter, ReleaseDisc Disc, Title Title);
