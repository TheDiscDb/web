using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Logging;
using TheDiscDb.Data.Changes.Track;
using TheDiscDb.InputModels;
using TheDiscDb.Services.EditSuggestions;
using TheDiscDb.Services.Server;
using TheDiscDb.Web.Data;
using EntityTrack = TheDiscDb.InputModels.Track;

namespace TheDiscDb.Components.Pages;

[Authorize]
public partial class TracksEdit : ComponentBase
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
    public ILogger<TracksEdit> Logger { get; set; } = null!;

    [CascadingParameter]
    public HttpContext? HttpContext { get; set; }

    private IDisplayItem? Item { get; set; }
    private Release? Release { get; set; }
    private ReleaseDisc? Disc { get; set; }
    private Title? Title { get; set; }

    // One editable row per track on the title. Only the Name is user-editable;
    // all other fields come from the disc file metadata and are shown read-only.
    private readonly List<TrackEditRow> rows = [];

    private string? summary;
    private string? submitMessage;
    private bool submitSuccess;
    private bool isSubmitting;
    private bool isReviewing;
    private List<TrackNameDiff> pendingDiffs = [];

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

        if (string.IsNullOrEmpty(Disc.Slug))
        {
            return;
        }

        string? mediaItemSlug = IsBoxset() ? null : Slug;
        string? boxsetSlug = IsBoxset() ? Slug : null;

        foreach (var track in Title.Tracks)
        {
            var snapshot = TrackFieldsUpdate.SnapshotFrom(
                track, mediaItemSlug, boxsetSlug, Release.Slug ?? string.Empty, Disc.Slug, Title.Index);
            rows.Add(new TrackEditRow(track, snapshot) { EditName = track.Name });
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

    private bool HasChanges() => rows.Any(r => r.HasChange);

    private bool AllNamesValid() =>
        rows.Where(r => r.HasChange).All(r => !string.IsNullOrWhiteSpace(r.EditName));

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

    private List<TrackNameDiff> ComputeDiffs()
    {
        return rows
            .Where(r => r.HasChange)
            .Select(r => new TrackNameDiff(
                r.Track.Index,
                r.Track.Type ?? "Track",
                GetMetadata(r.Track),
                r.Original.Name,
                r.EditName))
            .ToList();
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
                submitMessage = "You must be signed in to submit suggestions.";
                submitSuccess = false;
                return;
            }

            var changes = rows
                .Where(r => r.HasChange)
                .Select(r => new SubmitChangeInput(
                    TrackFieldsUpdate.Key,
                    JsonSerializer.Serialize(r.BuildProposed(), JsonOptions),
                    JsonSerializer.Serialize(r.Original, JsonOptions)))
                .ToList();

            if (changes.Count == 0)
            {
                submitMessage = "No changes to submit.";
                submitSuccess = false;
                return;
            }

            var result = await EditSuggestionService.SubmitAsync(userId, EditSuggestionSource.Web, summary, changes);

            var sqid = IdEncoder.Encode(result.Id);
            Navigation.NavigateTo(GetTitleUrl() + $"?editSubmitted={sqid}", forceLoad: true);
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Failed to submit track edit suggestions");
            submitSuccess = false;
            submitMessage = "Something went wrong while submitting your suggestion. Please try again.";
        }
        finally
        {
            isSubmitting = false;
        }
    }

    /// <summary>
    /// Builds the metadata description for a track without including its Name,
    /// so the context column stays stable while the user edits the name.
    /// </summary>
    private static string GetMetadata(EntityTrack track)
    {
        return track.Type?.ToLowerInvariant() switch
        {
            "video" => Join(track.AspectRatio, Parenthesize(track.Resolution)),
            "audio" => Join(track.AudioType, Parenthesize(track.Language)),
            "subtitles" => Parenthesize(track.Language),
            _ => string.Empty,
        };

        static string Parenthesize(string? value) =>
            string.IsNullOrEmpty(value) ? string.Empty : $"({value})";

        static string Join(string? a, string b) =>
            string.Join(" ", new[] { a, b }.Where(s => !string.IsNullOrEmpty(s))).Trim();
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

    private sealed class TrackEditRow(EntityTrack track, TrackFieldsDetails original)
    {
        public EntityTrack Track { get; } = track;

        public TrackFieldsDetails Original { get; } = original;

        public string? EditName { get; set; }

        public TrackFieldsDetails BuildProposed() => Original with { Name = EditName };

        public bool HasChange => Original.Name != EditName;
    }
}

internal sealed record TrackNameDiff(int Index, string Type, string Metadata, string? CurrentName, string? ProposedName);
