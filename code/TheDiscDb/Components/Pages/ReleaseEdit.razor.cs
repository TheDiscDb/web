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
using TheDiscDb.Services.Server;
using TheDiscDb.Validation;
using TheDiscDb.Web.Data;

namespace TheDiscDb.Components.Pages;

public class EditReleaseRequest
{
    [Required(ErrorMessage = "Release Title is required")]
    public string? Title { get; set; }

    [Required(ErrorMessage = "Region Code is required")]
    public string? RegionCode { get; set; }

    [Required(ErrorMessage = "Locale is required")]
    public string? Locale { get; set; }

    [Range(1900, 2100)]
    public int Year { get; set; }

    [Required(ErrorMessage = "UPC/EAN is required")]
    [Upc]
    public string? Upc { get; set; }
    public string? Isbn { get; set; }

    [Asin]
    public string? Asin { get; set; }

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
    public IdEncoder IdEncoder { get; set; } = null!;

    [Inject]
    public ILogger<ReleaseEdit> Logger { get; set; } = null!;

    [CascadingParameter]
    public HttpContext? HttpContext { get; set; }

    private MediaItem? Item { get; set; }
    private Release? Release { get; set; }
    private ReleaseFieldsDetails? originalSnapshot;

    private readonly EditReleaseRequest request = new();
    private string releaseDate = string.Empty;
    private string releaseDateValidationMessage = string.Empty;
    private string? submitMessage;
    private bool submitSuccess;
    private bool isSubmitting;
    private bool isReviewing;
    private List<FieldDiff> changedFields = [];

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
        releaseDate = Release.ReleaseDate.ToString("MM-dd-yyyy");
    }

    private void ShowReview()
    {
        if (originalSnapshot == null)
        {
            return;
        }

        if (!ValidateReleaseDate())
        {
            return;
        }

        changedFields = ComputeDiff();
        isReviewing = true;
    }

    // Mirrors the contribution release-detail form: accept the Amazon
    // "MMMM d, yyyy" format on input, otherwise hold the raw text and validate
    // it on submit.
    private void ReleaseDateChanged(ChangeEventArgs args)
    {
        var value = args?.Value?.ToString();
        releaseDateValidationMessage = string.Empty;

        if (!string.IsNullOrEmpty(value))
        {
            if (DateTimeOffset.TryParseExact(value, "MMMM d, yyyy", null,
                System.Globalization.DateTimeStyles.None, out var parsedDate))
            {
                request.ReleaseDate = parsedDate;
                releaseDate = parsedDate.ToString("MM-dd-yyyy");
            }
            else
            {
                releaseDate = value;
                request.ReleaseDate = DateTimeOffset.MinValue;
            }
        }
        else
        {
            releaseDate = string.Empty;
            request.ReleaseDate = DateTimeOffset.MinValue;
        }
    }

    private bool ValidateReleaseDate()
    {
        if (string.IsNullOrWhiteSpace(releaseDate))
        {
            releaseDateValidationMessage = "Release Date is required.";
            return false;
        }

        if (!DateTimeOffset.TryParse(releaseDate, out var date))
        {
            releaseDateValidationMessage = $"'{releaseDate}' is not a valid date.";
            return false;
        }

        request.ReleaseDate = date;
        releaseDateValidationMessage = string.Empty;
        return true;
    }

    private void BackToEdit()
    {
        isReviewing = false;
        submitMessage = null;
    }

    private async Task HandleSubmit()
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

            var result = await EditSuggestionService.SubmitAsync(userId, EditSuggestionSource.Web, request.Summary, changes);

            var sqid = IdEncoder.Encode(result.Id);
            var itemUrl = $"/{Type}/{Slug}/releases/{ReleaseSlug}?editSubmitted={sqid}";
            Navigation.NavigateTo(itemUrl, forceLoad: true);
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

    private List<FieldDiff> ComputeDiff()
    {
        if (originalSnapshot == null)
        {
            return [];
        }

        var diffs = new List<FieldDiff>();

        AddIfChanged(diffs, "Title", originalSnapshot.Title, request.Title);
        AddIfChanged(diffs, "Region Code", originalSnapshot.RegionCode, request.RegionCode);
        AddIfChanged(diffs, "Locale", originalSnapshot.Locale, request.Locale);
        AddIfChanged(diffs, "Year", originalSnapshot.Year.ToString(), request.Year.ToString());
        AddIfChanged(diffs, "UPC", originalSnapshot.Upc, request.Upc);
        AddIfChanged(diffs, "ISBN", originalSnapshot.Isbn, request.Isbn);
        AddIfChanged(diffs, "ASIN", originalSnapshot.Asin, request.Asin);
        AddIfChanged(diffs, "Release Date",
            originalSnapshot.ReleaseDate.ToString("d"),
            request.ReleaseDate.ToString("d"));

        return diffs;
    }

    private static void AddIfChanged(List<FieldDiff> diffs, string fieldName, string? currentValue, string? proposedValue)
    {
        var current = currentValue ?? string.Empty;
        var proposed = proposedValue ?? string.Empty;
        if (!string.Equals(current, proposed, StringComparison.Ordinal))
        {
            diffs.Add(new FieldDiff(fieldName, string.IsNullOrEmpty(current) ? "(empty)" : current, string.IsNullOrEmpty(proposed) ? "(empty)" : proposed));
        }
    }

    private bool IsBoxset() =>
        Type?.Equals("boxset", StringComparison.OrdinalIgnoreCase) == true;
}

internal sealed record FieldDiff(string FieldName, string CurrentValue, string ProposedValue);
