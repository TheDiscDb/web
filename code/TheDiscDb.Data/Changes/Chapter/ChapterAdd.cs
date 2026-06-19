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
/// Adds a new <see cref="EntityChapter"/> to an existing disc item.
/// The <see cref="ChapterDetails.ChapterIndex"/> specifies the index for the
/// new chapter. Validation ensures no chapter with that index already exists
/// on the target item.
/// </summary>
public sealed class ChapterAdd : ChangeBase<ChapterDetails>
{
    public const string Key = "chapter.add";

    public ChapterAdd(ChapterDetails proposed)
        : base(proposed)
    {
    }

    public ChapterAdd(ChapterDetails proposed, JsonSerializerOptions jsonOptions)
        : base(proposed, jsonOptions)
    {
    }

    public override string TypeKey => Key;

    public override string TargetEntityKey => this.Proposed.TargetEntityKey;

    /// <summary>Add types have no prior entity to snapshot.</summary>
    public override bool RequiresOriginalSnapshot => false;

    protected override async Task<ChapterDetails?> LoadCurrentSnapshotAsync(
        SqlServerDataContext context,
        CancellationToken cancellationToken)
    {
        // For adds, LoadCurrentSnapshot checks that the PARENT exists (the disc item)
        // and that the chapter index is NOT already taken. Return a non-null sentinel
        // to signal "parent exists, ready to add." Return null if parent is missing.
        var parent = await ResolveParentItemAsync(context, this.Proposed, cancellationToken);
        if (parent is null)
        {
            return null;
        }

        // Check for duplicate index
        var existing = parent.Item.Chapters.FirstOrDefault(c => c.Index == this.Proposed.ChapterIndex);
        if (existing is not null)
        {
            // Return a snapshot of the existing chapter — DescribeDrift will detect
            // the conflict as "chapter already exists."
            return ChapterUpdate.SnapshotFrom(
                existing,
                this.Proposed.MediaItemSlug,
                this.Proposed.BoxsetSlug,
                this.Proposed.ReleaseSlug,
                parent.Disc.Slug!,
                parent.Title.Index);
        }

        // Parent exists, index is free — return a sentinel snapshot representing
        // "empty slot." We use the proposed details itself (Validate sees proposed == current → NoOp
        // is wrong here). Actually, for adds, Validate's RequiresOriginalSnapshot = false
        // path just checks current != null and then proposed != current. We need to
        // return something that differs from Proposed so it's not NoOp.
        // Return the proposed shape but with Title nulled to signal "slot is empty."
        return new ChapterDetails(
            this.Proposed.MediaItemSlug,
            this.Proposed.BoxsetSlug,
            this.Proposed.ReleaseSlug,
            this.Proposed.DiscSlug,
            this.Proposed.TitleIndex,
            this.Proposed.ChapterIndex,
            Title: null);
    }

    protected override async Task ApplyCoreAsync(
        SqlServerDataContext context,
        IChangeApplyContext apply,
        ChapterDetails? original,
        CancellationToken cancellationToken)
    {
        var parent = await ResolveParentItemAsync(context, this.Proposed, cancellationToken)
            ?? throw new InvalidOperationException(
                $"Parent disc item for '{this.Proposed.TargetEntityKey}' not found at apply time.");

        // Guard: chapter index must still be available
        if (parent.Item.Chapters.Any(c => c.Index == this.Proposed.ChapterIndex))
        {
            throw new ChangeApplyConflictException(
                this.TypeKey,
                $"Chapter at index {this.Proposed.ChapterIndex} already exists on '{this.Proposed.TargetEntityKey}'.");
        }

        var chapter = new EntityChapter
        {
            Index = this.Proposed.ChapterIndex,
            Title = this.Proposed.Title
        };

        parent.Item.Chapters.Add(chapter);
    }

    protected override string MissingTargetMessage()
        => $"Parent disc item for '{this.Proposed.TargetEntityKey}' no longer exists.";

    protected override string? DescribeDrift(ChapterDetails original, ChapterDetails current)
    {
        // For adds, if LoadCurrentSnapshot returned a real chapter (duplicate index),
        // that means the slot is taken.
        if (current.Title is not null && original.Title is null)
        {
            return $"Chapter at index {this.Proposed.ChapterIndex} already exists with title '{current.Title}'.";
        }

        return null;
    }

    private static async Task<ResolvedParentItem?> ResolveParentItemAsync(
        SqlServerDataContext context,
        ChapterDetails details,
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
                .Include(r => r.Discs).ThenInclude(d => d.Titles).ThenInclude(t => t.Item).ThenInclude(i => i!.Chapters)
                .FirstOrDefaultAsync(
                    r => r.Slug == releaseSlug && r.MediaItem != null && r.MediaItem.Slug == ms,
                    cancellationToken);
        }
        else
        {
            var bs = details.BoxsetSlug;
            release = await context.Releases
                .Include(r => r.Discs).ThenInclude(d => d.Titles).ThenInclude(t => t.Item).ThenInclude(i => i!.Chapters)
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
        if (title?.Item is null)
        {
            return null;
        }

        return new ResolvedParentItem(title.Item, disc, title);
    }
}

internal sealed record ResolvedParentItem(DiscItemReference Item, ReleaseDisc Disc, Title Title);
