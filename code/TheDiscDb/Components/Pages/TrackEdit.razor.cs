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
public partial class TrackEdit : ComponentBase
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

    [Parameter]
    public int TrackIndex { get; set; }

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
    public ILogger<TrackEdit> Logger { get; set; } = null!;

    [CascadingParameter]
    public HttpContext? HttpContext { get; set; }

    private IDisplayItem? Item { get; set; }
    private Release? Release { get; set; }
    private ReleaseDisc? Disc { get; set; }
    private Title? Title { get; set; }
    private EntityTrack? Track { get; set; }

    // Editable fields
    private string? editName;
    private string? editType;
    private string? editResolution;
    private string? editAspectRatio;
    private string? editAudioType;
    private string? editLanguageCode;
    private string? editLanguage;
    private string? editDescription;

    private TrackFieldsDetails? originalSnapshot;

    private string? summary;
    private string? submitMessage;
    private bool submitSuccess;
    private bool isSubmitting;
    private bool isReviewing;
    private List<TrackFieldDiff> pendingDiffs = [];

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    protected override async Task OnInitializedAsync()
    {
        if (string.IsNullOrEmpty(Type) || string.IsNullOrEmpty(Slug))
        {
            return;
        }

        await LoadItemData();

        if (Item == null || Release == null || Disc == null || Title == null || Track == null)
        {
            if (HttpContext != null && !HttpContext.Response.HasStarted)
            {
                HttpContext.Response.StatusCode = StatusCodes.Status404NotFound;
            }

            return;
        }

        editName = Track.Name;
        editType = Track.Type;
        editResolution = Track.Resolution;
        editAspectRatio = Track.AspectRatio;
        editAudioType = Track.AudioType;
        editLanguageCode = Track.LanguageCode;
        editLanguage = Track.Language;
        editDescription = Track.Description;

        string? mediaItemSlug = IsBoxset() ? null : Slug;
        string? boxsetSlug = IsBoxset() ? Slug : null;
        originalSnapshot = TrackFieldsUpdate.SnapshotFrom(
            Track, mediaItemSlug, boxsetSlug, Release.Slug ?? string.Empty, Disc.Slug ?? string.Empty, Title.Index);
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

        if (Title != null)
        {
            Track = Title.Tracks.FirstOrDefault(t => t.Index == TrackIndex);
        }
    }

    /// <summary>
    /// Builds the proposed details, nulling fields that don't apply to the
    /// selected track type. This enforces mutual-exclusion so that re-typing a
    /// track (e.g. Audio → Video) clears the now-irrelevant fields in the diff.
    /// </summary>
    private TrackFieldsDetails BuildProposed()
    {
        bool isVideo = editType == "Video";
        bool isAudioOrSub = editType is "Audio" or "Subtitles";
        bool isAudio = editType == "Audio";

        return originalSnapshot! with
        {
            Name = editName,
            Type = editType,
            Resolution = isVideo ? editResolution : null,
            AspectRatio = isVideo ? editAspectRatio : null,
            AudioType = isAudio ? editAudioType : null,
            LanguageCode = isAudioOrSub ? editLanguageCode : null,
            Language = isAudioOrSub ? editLanguage : null,
            Description = isAudioOrSub ? editDescription : null,
        };
    }

    private bool HasChanges()
    {
        if (originalSnapshot == null)
        {
            return false;
        }

        return BuildProposed() != originalSnapshot;
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

    private List<TrackFieldDiff> ComputeDiffs()
    {
        var diffs = new List<TrackFieldDiff>();
        if (originalSnapshot == null)
        {
            return diffs;
        }

        var proposed = BuildProposed();

        AddDiffIfChanged(diffs, "Name", originalSnapshot.Name, proposed.Name);
        AddDiffIfChanged(diffs, "Type", originalSnapshot.Type, proposed.Type);
        AddDiffIfChanged(diffs, "Resolution", originalSnapshot.Resolution, proposed.Resolution);
        AddDiffIfChanged(diffs, "Aspect Ratio", originalSnapshot.AspectRatio, proposed.AspectRatio);
        AddDiffIfChanged(diffs, "Audio Type", originalSnapshot.AudioType, proposed.AudioType);
        AddDiffIfChanged(diffs, "Language", originalSnapshot.Language, proposed.Language);
        AddDiffIfChanged(diffs, "Language Code", originalSnapshot.LanguageCode, proposed.LanguageCode);
        AddDiffIfChanged(diffs, "Description", originalSnapshot.Description, proposed.Description);

        return diffs;
    }

    private static void AddDiffIfChanged(List<TrackFieldDiff> diffs, string fieldName, string? current, string? proposed)
    {
        if (current != proposed)
        {
            diffs.Add(new TrackFieldDiff(fieldName, current ?? "(empty)", proposed ?? "(empty)"));
        }
    }

    private async Task HandleSubmit()
    {
        if (Track == null || Title == null || Disc == null || Release == null || Item == null || originalSnapshot == null)
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

            var snapshotJson = JsonSerializer.Serialize(originalSnapshot, JsonOptions);
            var proposedJson = JsonSerializer.Serialize(BuildProposed(), JsonOptions);

            var changes = new List<SubmitChangeInput>
            {
                new(TrackFieldsUpdate.Key, proposedJson, snapshotJson),
            };

            var result = await EditSuggestionService.SubmitAsync(userId, EditSuggestionSource.Web, summary, changes);

            var sqid = IdEncoder.Encode(result.Id);
            Navigation.NavigateTo(GetTitleUrl() + $"?editSubmitted={sqid}", forceLoad: true);
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Failed to submit track edit suggestion");
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

internal sealed record TrackFieldDiff(string FieldName, string? CurrentValue, string? ProposedValue);
