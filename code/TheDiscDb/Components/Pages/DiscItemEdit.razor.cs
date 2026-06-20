using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Logging;
using TheDiscDb.Data.Changes.DiscItemFields;
using TheDiscDb.InputModels;
using TheDiscDb.Services.EditSuggestions;
using TheDiscDb.Services.Server;
using TheDiscDb.Web.Data;

namespace TheDiscDb.Components.Pages;

[Authorize]
public partial class DiscItemEdit : ComponentBase
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
    public ILogger<DiscItemEdit> Logger { get; set; } = null!;

    [CascadingParameter]
    public HttpContext? HttpContext { get; set; }

    private IDisplayItem? Item { get; set; }
    private Release? Release { get; set; }
    private ReleaseDisc? Disc { get; set; }
    private Title? Title { get; set; }

    // Editable fields — DiscItemReference
    private string? editItemTitle;
    private string? editItemType;
    private string? editItemDescription;
    private string? editItemSeason;
    private string? editItemEpisode;

    // Original snapshot values
    private string? originalItemTitle;
    private string? originalItemType;
    private string? originalItemDescription;
    private string? originalItemSeason;
    private string? originalItemEpisode;
    private bool originalHasItem;

    private string? summary;
    private string? submitMessage;
    private bool submitSuccess;
    private bool isSubmitting;
    private bool isReviewing;
    private List<DiscItemFieldDiff> pendingDiffs = [];

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

        // Populate editable fields from current state
        editItemTitle = Title.Item?.Title;
        editItemType = Title.Item?.Type;
        editItemDescription = Title.Item?.Description;
        editItemSeason = Title.Item?.Season;
        editItemEpisode = Title.Item?.Episode;

        // Capture original snapshot
        originalItemTitle = editItemTitle;
        originalItemType = editItemType;
        originalItemDescription = editItemDescription;
        originalItemSeason = editItemSeason;
        originalItemEpisode = editItemEpisode;
        originalHasItem = Title.Item is not null;
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

    private bool HasChanges()
    {
        return editItemTitle != originalItemTitle
            || editItemType != originalItemType
            || editItemDescription != originalItemDescription
            || editItemSeason != originalItemSeason
            || editItemEpisode != originalItemEpisode;
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

    private List<DiscItemFieldDiff> ComputeDiffs()
    {
        var diffs = new List<DiscItemFieldDiff>();

        AddDiffIfChanged(diffs, "Content Title", originalItemTitle, editItemTitle);
        AddDiffIfChanged(diffs, "Type", originalItemType, editItemType);
        AddDiffIfChanged(diffs, "Description", originalItemDescription, editItemDescription);
        AddDiffIfChanged(diffs, "Season", originalItemSeason, editItemSeason);
        AddDiffIfChanged(diffs, "Episode", originalItemEpisode, editItemEpisode);

        return diffs;
    }

    private static void AddDiffIfChanged(List<DiscItemFieldDiff> diffs, string fieldName, string? current, string? proposed)
    {
        if (current != proposed)
        {
            diffs.Add(new DiscItemFieldDiff(fieldName, current ?? "(empty)", proposed ?? "(empty)"));
        }
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

            var originalSnapshot = new DiscItemFieldsDetails(
                mediaItemSlug, boxsetSlug, Release.Slug ?? string.Empty,
                discSlug, Title.Index,
                Title.Comment, Title.SourceFile, Title.SegmentMap, Title.Duration,
                originalHasItem,
                originalItemTitle, originalItemType, originalItemDescription,
                originalItemSeason, originalItemEpisode);
            var snapshotJson = JsonSerializer.Serialize(originalSnapshot, JsonOptions);

            var proposed = new DiscItemFieldsDetails(
                mediaItemSlug, boxsetSlug, Release.Slug ?? string.Empty,
                discSlug, Title.Index,
                Title.Comment, Title.SourceFile, Title.SegmentMap, Title.Duration,
                originalHasItem || HasAnyItemField(),
                editItemTitle, editItemType, editItemDescription,
                editItemSeason, editItemEpisode);
            var proposedJson = JsonSerializer.Serialize(proposed, JsonOptions);

            var changes = new List<SubmitChangeInput>
            {
                new(DiscItemFieldsUpdate.Key, proposedJson, snapshotJson),
            };

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
            Logger.LogError(ex, "Failed to submit disc item edit suggestions");
            submitSuccess = false;
            submitMessage = "Something went wrong while submitting your suggestion. Please try again.";
        }
        finally
        {
            isSubmitting = false;
        }
    }

    private bool HasAnyItemField() =>
        !string.IsNullOrEmpty(editItemTitle)
        || !string.IsNullOrEmpty(editItemType)
        || !string.IsNullOrEmpty(editItemDescription)
        || !string.IsNullOrEmpty(editItemSeason)
        || !string.IsNullOrEmpty(editItemEpisode);

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

internal sealed record DiscItemFieldDiff(string FieldName, string? CurrentValue, string? ProposedValue);
