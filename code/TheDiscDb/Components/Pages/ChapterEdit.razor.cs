using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Logging;
using TheDiscDb.Data.Changes.Chapter;
using TheDiscDb.InputModels;
using TheDiscDb.Services.EditSuggestions;
using TheDiscDb.Services.Server;
using TheDiscDb.Web.Data;

namespace TheDiscDb.Components.Pages;

[Authorize]
public partial class ChapterEdit : ComponentBase
{
    [Parameter]
    public string? Type { get; set; }

    [Parameter]
    public string? Slug { get; set; }

    [Parameter]
    public string? ReleaseSlug { get; set; }

    [Parameter]
    public string? SlugOrIndex { get; set; }

    [Parameter]
    public string? File { get; set; }

    [Parameter]
    public string? Extension { get; set; }

    [Inject]
    public CacheHelper Cache { get; set; } = null!;

    [Inject]
    public IEditSuggestionService EditSuggestionService { get; set; } = null!;

    [Inject]
    public UserManager<TheDiscDbUser> UserManager { get; set; } = null!;

    [Inject]
    public AuthenticationStateProvider AuthStateProvider { get; set; } = null!;

    [Inject]
    public NavigationManager Navigation { get; set; } = null!;

    [Inject]
    public IdEncoder IdEncoder { get; set; } = null!;

    [Inject]
    public ILogger<ChapterEdit> Logger { get; set; } = null!;

    [CascadingParameter]
    public HttpContext? HttpContext { get; set; }

    private IDisplayItem? Item { get; set; }
    private Release? Release { get; set; }
    private ReleaseDisc? Disc { get; set; }
    private Title? Title { get; set; }

    private List<ChapterEditRow> chapters = [];

    // Original chapter titles in order, captured at load. Position-based: the
    // entry at index i is the title that was originally at chapter (i + 1).
    private List<string?> originalTitles = [];

    private ChapterEditRow? draggedChapter;
    private ChapterEditRow? dragOverChapter;

    private int bulkAddCount = 1;
    private string? summary;
    private string? submitMessage;
    private bool submitSuccess;
    private bool isSubmitting;
    private bool isReviewing;
    private List<ChapterDiff> pendingDiffs = [];

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    protected override async Task OnInitializedAsync()
    {
        if (string.IsNullOrEmpty(Type) || string.IsNullOrEmpty(Slug))
        {
            return;
        }

        await LoadItemData();

        if (Item == null || Release == null || Disc == null || Title == null)
        {
            if (HttpContext != null && !HttpContext.Response.HasStarted)
            {
                HttpContext.Response.StatusCode = StatusCodes.Status404NotFound;
            }

            return;
        }

        // Load existing chapters into editable rows
        if (Title.Item?.Chapters != null)
        {
            foreach (var chapter in Title.Item.Chapters.OrderBy(c => c.Index))
            {
                chapters.Add(new ChapterEditRow
                {
                    Title = chapter.Title,
                    OriginalTitle = chapter.Title,
                    IsNew = false,
                    IsDeleted = false,
                });
                originalTitles.Add(chapter.Title);
            }
        }
    }

    private async Task LoadItemData()
    {
        if (Type!.Equals("Boxset", StringComparison.OrdinalIgnoreCase))
        {
            var boxset = await Cache.GetBoxsetAsync(Slug!);
            Item = boxset;

            if (boxset == null)
            {
                return;
            }

            Release = boxset.Release;
        }
        else
        {
            var item = await Cache.GetMediaItemDetail(Type!, Slug!);
            Item = item;

            if (item == null)
            {
                return;
            }

            Release = item.Releases.FirstOrDefault(r =>
                !string.IsNullOrEmpty(r.Slug) && r.Slug.Equals(ReleaseSlug, StringComparison.OrdinalIgnoreCase));
        }

        if (Release == null)
        {
            return;
        }

        Disc = Release.Discs.FirstOrDefault(d =>
            TheDiscDb.SlugOrIndex.Create(d.Slug, d.Index) == TheDiscDb.SlugOrIndex.Create(SlugOrIndex));

        if (Disc != null && !string.IsNullOrEmpty(File))
        {
            string sourceFile = NavigationExtensions.GetSourceFile(File, Extension);
            Title = Disc.Titles.FirstOrDefault(t =>
                !string.IsNullOrEmpty(t.SourceFile) && t.SourceFile.Equals(sourceFile, StringComparison.OrdinalIgnoreCase));
        }
    }

    private void BulkAddChapters()
    {
        for (int i = 0; i < bulkAddCount; i++)
        {
            chapters.Add(new ChapterEditRow
            {
                Title = null,
                OriginalTitle = null,
                IsNew = true,
                IsDeleted = false,
            });
        }
    }

    private void AddOneChapter()
    {
        chapters.Add(new ChapterEditRow
        {
            Title = null,
            OriginalTitle = null,
            IsNew = true,
            IsDeleted = false,
        });
    }

    private void OnDragStart(ChapterEditRow chapter)
    {
        if (chapter.IsDeleted)
        {
            return;
        }

        draggedChapter = chapter;
    }

    private void OnDragEnter(ChapterEditRow chapter)
    {
        if (draggedChapter == null || chapter == draggedChapter || chapter.IsDeleted)
        {
            return;
        }

        dragOverChapter = chapter;

        int fromIndex = chapters.IndexOf(draggedChapter);
        int toIndex = chapters.IndexOf(chapter);
        if (fromIndex < 0 || toIndex < 0)
        {
            return;
        }

        chapters.RemoveAt(fromIndex);
        chapters.Insert(toIndex, draggedChapter);
    }

    private void OnDragEnd()
    {
        draggedChapter = null;
        dragOverChapter = null;
    }

    private string GetRowClass(ChapterEditRow chapter)
    {
        if (chapter.IsDeleted)
        {
            return "table-danger text-decoration-line-through";
        }

        if (chapter == draggedChapter)
        {
            return "dragging";
        }

        if (chapter == dragOverChapter)
        {
            return "drag-over";
        }

        if (chapter.IsNew)
        {
            return "table-success";
        }

        return string.Empty;
    }

    private void MarkDeleted(ChapterEditRow chapter) => chapter.IsDeleted = true;
    private void UndoDelete(ChapterEditRow chapter) => chapter.IsDeleted = false;
    private void RemoveNew(ChapterEditRow chapter) => chapters.Remove(chapter);

    private bool HasChanges() => ComputeChanges().Count > 0;

    /// <summary>
    /// Computes the set of chapter changes by comparing the original chapter
    /// titles (captured at load, by position) against the current proposed
    /// order. Because chapters are identified by their 1-based position, a
    /// reorder naturally surfaces as title updates at the affected positions,
    /// while adds/deletes fall out of the length difference.
    /// </summary>
    private List<ChapterChange> ComputeChanges()
    {
        var changes = new List<ChapterChange>();
        var proposed = chapters.Where(c => !c.IsDeleted).ToList();
        int max = Math.Max(originalTitles.Count, proposed.Count);

        for (int i = 0; i < max; i++)
        {
            int index = i + 1;
            bool hasOriginal = i < originalTitles.Count;
            bool hasProposed = i < proposed.Count;
            string? original = hasOriginal ? originalTitles[i] : null;
            string? proposedTitle = hasProposed ? proposed[i].Title : null;

            if (hasProposed && !hasOriginal)
            {
                changes.Add(new ChapterChange(index, ChapterChangeKind.Add, null, proposedTitle));
            }
            else if (!hasProposed && hasOriginal)
            {
                changes.Add(new ChapterChange(index, ChapterChangeKind.Delete, original, null));
            }
            else if (hasProposed && hasOriginal &&
                !string.Equals(original ?? string.Empty, proposedTitle ?? string.Empty, StringComparison.Ordinal))
            {
                changes.Add(new ChapterChange(index, ChapterChangeKind.Update, original, proposedTitle));
            }
        }

        return changes;
    }

    private void ShowReview()
    {
        pendingDiffs = ComputeDiffs();
        isReviewing = true;
    }

    private void BackToEdit()
    {
        isReviewing = false;
        submitMessage = null;
    }

    private List<ChapterDiff> ComputeDiffs()
    {
        return ComputeChanges().Select(change => change.Kind switch
        {
            ChapterChangeKind.Add =>
                new ChapterDiff(change.Index, "Add", null, change.ProposedTitle ?? "(unnamed)", "table-success"),
            ChapterChangeKind.Delete =>
                new ChapterDiff(change.Index, "Delete", change.OriginalTitle ?? "(unnamed)", null, "table-danger"),
            _ =>
                new ChapterDiff(change.Index, "Edit", change.OriginalTitle ?? "(empty)", change.ProposedTitle ?? "(empty)", "table-warning"),
        }).ToList();
    }

    private async Task HandleSubmit()
    {
        if (Title == null || Disc == null || Release == null || Item == null)
        {
            return;
        }

        isSubmitting = true;
        submitMessage = null;

        try
        {
            var authState = await AuthStateProvider.GetAuthenticationStateAsync();
            var userId = UserManager.GetUserId(authState.User);
            if (string.IsNullOrEmpty(userId))
            {
                submitMessage = "You must be logged in to suggest edits.";
                return;
            }

            string? mediaItemSlug = IsBoxset() ? null : Slug;
            string? boxsetSlug = IsBoxset() ? Slug : null;
            string discSlug = Disc.Slug ?? string.Empty;
            int titleIndex = Title.Index;

            var changes = new List<SubmitChangeInput>();

            foreach (var change in ComputeChanges())
            {
                switch (change.Kind)
                {
                    case ChapterChangeKind.Add:
                        var addProposed = new ChapterDetails(
                            mediaItemSlug, boxsetSlug, Release.Slug ?? string.Empty,
                            discSlug, titleIndex, change.Index, change.ProposedTitle);
                        changes.Add(new SubmitChangeInput(
                            ChapterAdd.Key, JsonSerializer.Serialize(addProposed, JsonOptions), null));
                        break;

                    case ChapterChangeKind.Delete:
                        var deleteSnapshot = new ChapterDetails(
                            mediaItemSlug, boxsetSlug, Release.Slug ?? string.Empty,
                            discSlug, titleIndex, change.Index, change.OriginalTitle);
                        var deleteDetails = new ChapterDeleteDetails(
                            mediaItemSlug, boxsetSlug, Release.Slug ?? string.Empty,
                            discSlug, titleIndex, change.Index);
                        changes.Add(new SubmitChangeInput(
                            ChapterDelete.Key,
                            JsonSerializer.Serialize(deleteDetails, JsonOptions),
                            JsonSerializer.Serialize(deleteSnapshot, JsonOptions)));
                        break;

                    case ChapterChangeKind.Update:
                        var updateSnapshot = new ChapterDetails(
                            mediaItemSlug, boxsetSlug, Release.Slug ?? string.Empty,
                            discSlug, titleIndex, change.Index, change.OriginalTitle);
                        var updateProposed = new ChapterDetails(
                            mediaItemSlug, boxsetSlug, Release.Slug ?? string.Empty,
                            discSlug, titleIndex, change.Index, change.ProposedTitle);
                        changes.Add(new SubmitChangeInput(
                            ChapterUpdate.Key,
                            JsonSerializer.Serialize(updateProposed, JsonOptions),
                            JsonSerializer.Serialize(updateSnapshot, JsonOptions)));
                        break;
                }
            }

            if (changes.Count == 0)
            {
                submitMessage = "No changes to submit.";
                return;
            }

            var result = await EditSuggestionService.SubmitAsync(userId, EditSuggestionSource.Web, summary, changes);

            var sqid = IdEncoder.Encode(result.Id);
            var titleUrl = $"/{Type}/{Slug}/releases/{ReleaseSlug}/discs/{SlugOrIndex}/{File}";
            if (!string.IsNullOrEmpty(Extension))
            {
                titleUrl += $"/{Extension}";
            }
            titleUrl += $"?editSubmitted={sqid}";
            Navigation.NavigateTo(titleUrl, forceLoad: true);
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Failed to submit chapter edit suggestions");
            submitSuccess = false;
            submitMessage = "Something went wrong while submitting your suggestion. Please try again.";
        }
        finally
        {
            isSubmitting = false;
        }
    }

    private string GetTitleUrl()
    {
        if (Item == null || Release == null || Disc == null || Title == null)
        {
            return "/";
        }

        var link = BreadCrumbHelper.GetDiscTitleLink(Item, Release, Disc, Title.SourceFile);
        return link.Url;
    }

    private List<(string Text, string Url)> GetBreadcrumbs()
    {
        var items = new List<(string Text, string Url)>();
        if (Item != null)
        {
            items.Add(BreadCrumbHelper.GetRootLink(Item));
            items.Add(BreadCrumbHelper.GetMediaItemLink(Item));
        }

        if (Item != null && Release != null)
        {
            items.Add(BreadCrumbHelper.GetReleaseLink(Item, Release));
        }

        if (Item != null && Release != null && Disc != null)
        {
            items.Add(BreadCrumbHelper.GetDiscLink(Item, Release, Disc));
        }

        if (Item != null && Release != null && Disc != null && Title != null)
        {
            items.Add(BreadCrumbHelper.GetDiscTitleLink(Item, Release, Disc, Title.SourceFile));
        }

        return items;
    }

    private bool IsBoxset() =>
        Type?.Equals("boxset", StringComparison.OrdinalIgnoreCase) == true;
}

internal sealed class ChapterEditRow
{
    public string? Title { get; set; }
    public string? OriginalTitle { get; set; }
    public bool IsNew { get; set; }
    public bool IsDeleted { get; set; }
}

internal enum ChapterChangeKind
{
    Add,
    Delete,
    Update,
}

internal sealed record ChapterChange(int Index, ChapterChangeKind Kind, string? OriginalTitle, string? ProposedTitle);

internal sealed record ChapterDiff(int ChapterIndex, string ChangeType, string? CurrentValue, string? ProposedValue, string CssClass);
