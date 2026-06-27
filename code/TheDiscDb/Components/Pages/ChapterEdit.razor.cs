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
                    Index = chapter.Index,
                    Title = chapter.Title,
                    OriginalTitle = chapter.Title,
                    IsNew = false,
                    IsDeleted = false,
                });
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
        for (int i = 1; i <= bulkAddCount; i++)
        {
            if (!chapters.Any(c => c.Index == i && !c.IsDeleted))
            {
                chapters.Add(new ChapterEditRow
                {
                    Index = i,
                    Title = null,
                    OriginalTitle = null,
                    IsNew = true,
                    IsDeleted = false,
                });
            }
        }
    }

    private void FillMissingChapters()
    {
        var existingIndices = chapters.Where(c => !c.IsDeleted).Select(c => c.Index).ToHashSet();
        int max = chapters.Count > 0 ? chapters.Max(c => c.Index) : 0;
        for (int i = 1; i <= max; i++)
        {
            if (!existingIndices.Contains(i))
            {
                chapters.Add(new ChapterEditRow
                {
                    Index = i,
                    Title = null,
                    OriginalTitle = null,
                    IsNew = true,
                    IsDeleted = false,
                });
            }
        }
    }

    private void AddOneChapter()
    {
        int nextIndex = chapters.Count > 0
            ? chapters.Max(c => c.Index) + 1
            : 1;

        chapters.Add(new ChapterEditRow
        {
            Index = nextIndex,
            Title = null,
            OriginalTitle = null,
            IsNew = true,
            IsDeleted = false,
        });
    }

    private void MarkDeleted(ChapterEditRow chapter) => chapter.IsDeleted = true;
    private void UndoDelete(ChapterEditRow chapter) => chapter.IsDeleted = false;
    private void RemoveNew(ChapterEditRow chapter) => chapters.Remove(chapter);

    private bool HasChanges()
    {
        // Any new chapters?
        if (chapters.Any(c => c.IsNew && !c.IsDeleted))
        {
            return true;
        }

        // Any deleted chapters?
        if (chapters.Any(c => c.IsDeleted && !c.IsNew))
        {
            return true;
        }

        // Any edited chapters?
        if (chapters.Any(c => !c.IsNew && !c.IsDeleted && c.Title != c.OriginalTitle))
        {
            return true;
        }

        return false;
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
        var diffs = new List<ChapterDiff>();

        foreach (var chapter in chapters.OrderBy(c => c.Index))
        {
            if (chapter.IsNew && !chapter.IsDeleted)
            {
                diffs.Add(new ChapterDiff(chapter.Index, "Add", null, chapter.Title ?? "(unnamed)", "table-success"));
            }
            else if (chapter.IsDeleted && !chapter.IsNew)
            {
                diffs.Add(new ChapterDiff(chapter.Index, "Delete", chapter.OriginalTitle ?? "(unnamed)", null, "table-danger"));
            }
            else if (!chapter.IsNew && !chapter.IsDeleted && chapter.Title != chapter.OriginalTitle)
            {
                diffs.Add(new ChapterDiff(chapter.Index, "Edit",
                    chapter.OriginalTitle ?? "(empty)",
                    chapter.Title ?? "(empty)",
                    "table-warning"));
            }
        }

        return diffs;
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

            foreach (var chapter in chapters.OrderBy(c => c.Index))
            {
                if (chapter.IsNew && !chapter.IsDeleted)
                {
                    // Add
                    var proposed = new ChapterDetails(
                        mediaItemSlug, boxsetSlug, Release.Slug ?? string.Empty,
                        discSlug, titleIndex, chapter.Index, chapter.Title);

                    var proposedJson = JsonSerializer.Serialize(proposed, JsonOptions);
                    changes.Add(new SubmitChangeInput(ChapterAdd.Key, proposedJson, null));
                }
                else if (chapter.IsDeleted && !chapter.IsNew)
                {
                    // Delete — original snapshot is the full chapter details for conflict detection
                    var originalSnapshot = new ChapterDetails(
                        mediaItemSlug, boxsetSlug, Release.Slug ?? string.Empty,
                        discSlug, titleIndex, chapter.Index, chapter.OriginalTitle);
                    var snapshotJson = JsonSerializer.Serialize(originalSnapshot, JsonOptions);

                    var deleteDetails = new ChapterDeleteDetails(
                        mediaItemSlug, boxsetSlug, Release.Slug ?? string.Empty,
                        discSlug, titleIndex, chapter.Index);
                    var proposedJson = JsonSerializer.Serialize(deleteDetails, JsonOptions);

                    changes.Add(new SubmitChangeInput(ChapterDelete.Key, proposedJson, snapshotJson));
                }
                else if (!chapter.IsNew && !chapter.IsDeleted && chapter.Title != chapter.OriginalTitle)
                {
                    // Update
                    var originalSnapshot = new ChapterDetails(
                        mediaItemSlug, boxsetSlug, Release.Slug ?? string.Empty,
                        discSlug, titleIndex, chapter.Index, chapter.OriginalTitle);
                    var snapshotJson = JsonSerializer.Serialize(originalSnapshot, JsonOptions);

                    var proposed = new ChapterDetails(
                        mediaItemSlug, boxsetSlug, Release.Slug ?? string.Empty,
                        discSlug, titleIndex, chapter.Index, chapter.Title);
                    var proposedJson = JsonSerializer.Serialize(proposed, JsonOptions);

                    changes.Add(new SubmitChangeInput(ChapterUpdate.Key, proposedJson, snapshotJson));
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
    public int Index { get; set; }
    public string? Title { get; set; }
    public string? OriginalTitle { get; set; }
    public bool IsNew { get; set; }
    public bool IsDeleted { get; set; }
}

internal sealed record ChapterDiff(int ChapterIndex, string ChangeType, string? CurrentValue, string? ProposedValue, string CssClass);
