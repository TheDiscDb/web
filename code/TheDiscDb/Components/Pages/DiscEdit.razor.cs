using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Logging;
using TheDiscDb.Data.Changes.DiscFields;
using TheDiscDb.InputModels;
using TheDiscDb.Services.EditSuggestions;
using TheDiscDb.Services.Server;
using TheDiscDb.Web.Data;

namespace TheDiscDb.Components.Pages;

[Authorize]
public partial class DiscEdit : AuthenticatedComponentBase
{
    [Parameter]
    public string? Type { get; set; }

    [Parameter]
    public string? Slug { get; set; }

    [Parameter]
    public string? ReleaseSlug { get; set; }

    [Parameter]
    public string? SlugOrIndex { get; set; }

    [Inject]
    public CacheHelper Cache { get; set; } = null!;

    [Inject]
    public IEditSuggestionService EditSuggestionService { get; set; } = null!;

    [Inject]
    public NavigationManager Navigation { get; set; } = null!;

    [Inject]
    public IdEncoder IdEncoder { get; set; } = null!;

    [Inject]
    public ILogger<DiscEdit> Logger { get; set; } = null!;

    [CascadingParameter]
    public HttpContext? HttpContext { get; set; }

    private IDisplayItem? Item { get; set; }
    private Release? Release { get; set; }
    private ReleaseDisc? Disc { get; set; }

    private string? editName;
    private string? editFormat;

    private string? originalName;
    private string? originalFormat;

    private string? summary;
    private string? submitMessage;
    private bool submitSuccess;
    private bool isSubmitting;
    private bool isReviewing;
    private List<DiscFieldDiff> pendingDiffs = [];

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    protected override async Task OnInitializedAsync()
    {
        // Handle boxset URL format where Type is not in the route
        if (string.IsNullOrEmpty(Type) && !string.IsNullOrEmpty(Slug) && !string.IsNullOrEmpty(SlugOrIndex))
        {
            Type = "boxset";
        }

        if (string.IsNullOrEmpty(Type) || string.IsNullOrEmpty(Slug))
        {
            return;
        }

        await LoadItemData();

        if (Item == null || Release == null || Disc == null)
        {
            if (HttpContext != null && !HttpContext.Response.HasStarted)
            {
                HttpContext.Response.StatusCode = StatusCodes.Status404NotFound;
            }

            return;
        }

        editName = Disc.Name;
        editFormat = Disc.Format;

        originalName = editName;
        originalFormat = editFormat;
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
    }

    private bool HasChanges()
    {
        return editName != originalName
            || editFormat != originalFormat;
    }

    private bool IsValid()
    {
        return !string.IsNullOrWhiteSpace(editName)
            && !string.IsNullOrWhiteSpace(editFormat);
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

    private List<DiscFieldDiff> ComputeDiffs()
    {
        var diffs = new List<DiscFieldDiff>();

        AddDiffIfChanged(diffs, "Name", originalName, editName);
        AddDiffIfChanged(diffs, "Format", originalFormat, editFormat);

        return diffs;
    }

    private static void AddDiffIfChanged(List<DiscFieldDiff> diffs, string fieldName, string? current, string? proposed)
    {
        if (current != proposed)
        {
            diffs.Add(new DiscFieldDiff(fieldName, current ?? "(empty)", proposed ?? "(empty)"));
        }
    }

    private async Task HandleSubmit()
    {
        if (Disc == null || Release == null || Item == null)
        {
            return;
        }

        isSubmitting = true;
        submitMessage = null;

        try
        {
            var userId = await GetCurrentUserIdAsync();
            if (string.IsNullOrEmpty(userId))
            {
                submitMessage = "You must be signed in to submit suggestions.";
                submitSuccess = false;
                return;
            }

            string? mediaItemSlug = IsBoxset() ? null : Slug;
            string? boxsetSlug = IsBoxset() ? Slug : null;

            var originalSnapshot = DiscFieldsUpdate.SnapshotFrom(Disc, mediaItemSlug, boxsetSlug, Release.Slug ?? string.Empty);
            var snapshotJson = JsonSerializer.Serialize(originalSnapshot, JsonOptions);

            var proposed = new DiscFieldsDetails(
                mediaItemSlug, boxsetSlug, Release.Slug ?? string.Empty,
                Disc.Slug, Disc.Index,
                editName, editFormat);
            var proposedJson = JsonSerializer.Serialize(proposed, JsonOptions);

            var changes = new List<SubmitChangeInput>
            {
                new(DiscFieldsUpdate.Key, proposedJson, snapshotJson),
            };

            var result = await EditSuggestionService.SubmitAsync(userId, EditSuggestionSource.Web, summary, changes);

            var sqid = IdEncoder.Encode(result.Id);
            var discUrl = GetDiscUrl() + $"?editSubmitted={sqid}";
            Navigation.NavigateTo(discUrl, forceLoad: true);
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Failed to submit disc edit suggestions");
            submitSuccess = false;
            submitMessage = "Something went wrong while submitting your suggestion. Please try again.";
        }
        finally
        {
            isSubmitting = false;
        }
    }

    private string GetDiscUrl()
    {
        if (Item == null || Release == null || Disc == null)
        {
            return "/";
        }

        var link = BreadCrumbHelper.GetDiscLink(Item, Release, Disc);
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

        return items;
    }

    private bool IsBoxset() =>
        Type?.Equals("boxset", StringComparison.OrdinalIgnoreCase) == true;
}

internal sealed record DiscFieldDiff(string FieldName, string? CurrentValue, string? ProposedValue);
