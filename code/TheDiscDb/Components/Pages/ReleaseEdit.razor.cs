using System.ComponentModel.DataAnnotations;
using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Logging;
using TheDiscDb.Data.Changes.ReleaseFields;
using TheDiscDb.InputModels;
using TheDiscDb.Services.EditSuggestions;
using TheDiscDb.Web.Data;

namespace TheDiscDb.Components.Pages;

public class EditReleaseRequest
{
    public string? Title { get; set; }
    public string? RegionCode { get; set; }
    public string? Locale { get; set; }

    [Range(1900, 2100)]
    public int Year { get; set; }

    public string? Upc { get; set; }
    public string? Isbn { get; set; }
    public string? Asin { get; set; }

    [Required]
    public DateTimeOffset ReleaseDate { get; set; }

    public string? Summary { get; set; }
}

[Authorize]
public partial class ReleaseEdit : ComponentBase
{
    [Parameter]
    public string? Type { get; set; }

    [Parameter]
    public string? Slug { get; set; }

    [Parameter]
    public string? ReleaseSlug { get; set; }

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
    public ILogger<ReleaseEdit> Logger { get; set; } = null!;

    [CascadingParameter]
    public HttpContext? HttpContext { get; set; }

    private MediaItem? Item { get; set; }
    private Release? Release { get; set; }
    private ReleaseFieldsDetails? originalSnapshot;

    private readonly EditReleaseRequest request = new();
    private string? submitMessage;
    private bool submitSuccess;
    private bool isSubmitting;

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    protected override async Task OnInitializedAsync()
    {
        if (Type != null && Slug != null)
        {
            Item = await Cache.GetMediaItemDetail(Type, Slug);
        }

        if (Item != null)
        {
            Release = Item.Releases.FirstOrDefault(
                r => !string.IsNullOrEmpty(r.Slug) && r.Slug.Equals(ReleaseSlug, StringComparison.OrdinalIgnoreCase));
        }

        if (Item == null || Release == null)
        {
            if (HttpContext != null && !HttpContext.Response.HasStarted)
            {
                HttpContext.Response.StatusCode = StatusCodes.Status404NotFound;
            }

            return;
        }

        // Determine parent slug based on route type.
        string? mediaItemSlug = IsBoxset() ? null : Slug;
        string? boxsetSlug = IsBoxset() ? Slug : null;

        // Capture original snapshot for drift detection.
        originalSnapshot = ReleaseFieldsUpdate.SnapshotFrom(Release, mediaItemSlug, boxsetSlug);

        // Pre-fill the form with current values.
        request.Title = Release.Title;
        request.RegionCode = Release.RegionCode;
        request.Locale = Release.Locale;
        request.Year = Release.Year;
        request.Upc = Release.Upc;
        request.Isbn = Release.Isbn;
        request.Asin = Release.Asin;
        request.ReleaseDate = Release.ReleaseDate;
    }

    private async Task HandleValidSubmit()
    {
        if (Release == null || originalSnapshot == null)
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

            var proposed = new ReleaseFieldsDetails(
                MediaItemSlug: mediaItemSlug,
                BoxsetSlug: boxsetSlug,
                ReleaseSlug: Release.Slug ?? string.Empty,
                Title: request.Title,
                RegionCode: request.RegionCode,
                Locale: request.Locale,
                Year: request.Year,
                Upc: request.Upc,
                Isbn: request.Isbn,
                Asin: request.Asin,
                ReleaseDate: request.ReleaseDate);

            var proposedJson = JsonSerializer.Serialize(proposed, JsonOptions);
            var snapshotJson = JsonSerializer.Serialize(originalSnapshot, JsonOptions);

            var changes = new List<SubmitChangeInput>
            {
                new(ReleaseFieldsUpdate.Key, proposedJson, snapshotJson),
            };

            await EditSuggestionService.SubmitAsync(userId, EditSuggestionSource.Web, request.Summary, changes);

            submitSuccess = true;
            submitMessage = "Your edit suggestion has been submitted for review. Thank you!";
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Failed to submit edit suggestion for release {ReleaseSlug}", ReleaseSlug);
            submitSuccess = false;
            submitMessage = "Something went wrong while submitting your suggestion. Please try again.";
        }
        finally
        {
            isSubmitting = false;
        }
    }

    private bool IsBoxset() =>
        Type?.Equals("boxset", StringComparison.OrdinalIgnoreCase) == true;
}
