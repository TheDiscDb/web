using System.Reflection.Metadata;
using System.Web;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Components;
using StrawberryShake;
using Syncfusion.Blazor.Inputs;
using Syncfusion.Blazor.Notifications;
using TheDiscDb.Client.Contributions;
using TheDiscDb.Client.Controls;

namespace TheDiscDb.Client.Pages.Contribute;

[Authorize]
public partial class ReleaseDetailInput : CancellableComponentBase
{
    [Parameter]
    public string? MediaType { get; set; }

    [Parameter]
    public string? ExternalId { get; set; }

    [SupplyParameterFromQuery(Name = "boxsetId")]
    public string? BoxsetId { get; set; }

    [Inject]
    public IContributionClient ContributionClient { get; set; } = default!;

    [Inject]
    public ITheDiscDbClient TheDiscDbClient { get; set; } = default!;

    [Inject]
    public NavigationManager NavigationManager { get; set; } = default!;

    [Inject]
    public HttpClient HttpClient { get; set; } = default!;

    private SlugInput? slugInput;

    private readonly ContributionMutationRequestInput request = new ContributionMutationRequestInput
    {
        Locale = "en-us",
        RegionCode = "1",
        Status = UserContributionStatus.Pending
    };

    // EditForm binds to this wrapper for client-side DataAnnotations validation.
    // All properties pass through to `request`, so existing read/write sites are
    // unaffected.
    private ReleaseDetailFormBindings? form;

    private string releaseDate = string.Empty;
    private string releaseDateValidationMessage = string.Empty;
    private IGetExternalData_ExternalData_ExternalMetadata? externalData;
    private readonly Guid id = Guid.NewGuid();
    private string frontImageUploadUrl => $"/api/contribute/images/front/upload/{id}";
    private string frontImageRemoveUrl => $"/api/contribute/images/front/remove/{id}";
    private string backImageUploadUrl => $"/api/contribute/images/back/upload/{id}";
    private string backImageRemoveUrl => $"/api/contribute/images/back/remove/{id}";
    string frontImagePreviewUrl = "";
    string backImagePreviewUrl = "";
    private string? boxsetTitle;
    private string? intakePrefillMessage;
    private string BreadcrumbText => $"{this.externalData!.Title} ({this.externalData!.Year}) Details";
    private ContributionNamingSuggestion? ReleaseNamingSuggestion =>
        ContributionInputGuard.GetNamingSuggestion(
            this.externalData?.Title,
            this.form?.ReleaseTitle,
            this.form?.ReleaseSlug,
            title => HttpUtility.UrlEncode(CreateSlug(title, GetReleaseSlugYear(title))));
    private static readonly System.Text.RegularExpressions.Regex AsinRegex = new(@"^\w{10}$", System.Text.RegularExpressions.RegexOptions.Compiled);
    private bool ImportFromAmazonDisabled => this.form?.AsinNotAvailable == true || !AsinRegex.IsMatch(this.request.Asin ?? string.Empty) || IsAmazonImportInProgress;
    private bool IsAmazonImportInProgress = false;

    private SfUploader? frontImageUploader;
    private SfUploader? backImageUploader;
    SfToast? toast;
#pragma warning disable IDE0044 // Add readonly modifier
    string? toastContent = "Test";
#pragma warning restore IDE0044 // Add readonly modifier

    // Tracks the BoxsetId we last applied to the form so OnParametersSetAsync can detect
    // when the user navigates between linked/standalone (same route, different query string)
    // and re-sync the form model. Without this, Blazor reuses the component instance and
    // submits the stale BoxsetId.
    private string? lastAppliedBoxsetId;

    protected override async Task OnInitializedAsync()
    {
        this.form = new ReleaseDetailFormBindings(this.request);
        this.request.MediaType = this.MediaType ?? "Movie";
        this.request.ExternalProvider = "TMDB";
        this.request.ExternalId = this.ExternalId ?? string.Empty;
        this.request.StorageId = this.id;

        var result = await this.ContributionClient.GetExternalData.ExecuteAsync(new ExternalDataInput
        {
            ExternalId = this.request.ExternalId,
            MediaType = this.request.MediaType,
            Provider = this.request.ExternalProvider
        });

        if (result != null && result.IsSuccessResult())
        {
            this.externalData = result.Data!.ExternalData.ExternalMetadata;
            this.request.Title = this.externalData!.Title;
            this.request.Year = this.externalData!.Year.ToString();
        }
        else
        {
            this.NavigationManager.NavigateTo($"/contribution/externalIdNotFound/{this.request.ExternalId}");
        }

        // TODO: Check for other releases in the database for this ExternalId - then prompt or redirect?
    }

    protected override async Task OnParametersSetAsync()
    {
        // Re-sync boxset linkage whenever the route's query string changes. Blazor reuses the
        // component instance across same-route navigations, so OnInitializedAsync only fires
        // once. Without this, the "create as standalone" link wouldn't actually clear the
        // BoxsetId from the form model and submit would still link to the boxset.
        if (this.BoxsetId != this.lastAppliedBoxsetId)
        {
            this.lastAppliedBoxsetId = this.BoxsetId;
            this.request.BoxsetId = this.BoxsetId;

            if (string.IsNullOrEmpty(this.BoxsetId))
            {
                this.boxsetTitle = null;
            }
            else
            {
                // Reset banner state before lookup so a failed/empty result doesn't leave stale
                // title text from a previously linked boxset visible in the UI.
                this.boxsetTitle = null;

                var boxsetFilter = new UserContributionBoxsetFilterInput
                {
                    EncodedId = new EncodedIdOperationFilterInput { Eq = this.BoxsetId }
                };
                var boxsetResult = await this.ContributionClient.GetBoxsetDetail.ExecuteAsync(boxsetFilter);
                var boxset = boxsetResult?.Data?.MyBoxsets?.Nodes?.FirstOrDefault();
                if (boxset != null)
                {
                    this.boxsetTitle = boxset.Title;
                    // Prefill release-level fields from the boxset so the user doesn't re-enter
                    // shared metadata. The user can edit anything before submit.
                    this.request.ReleaseSlug = boxset.Slug ?? string.Empty;
                    this.request.ReleaseTitle = boxset.Title ?? string.Empty;
                    if (boxset.ReleaseDate.HasValue)
                    {
                        this.request.ReleaseDate = boxset.ReleaseDate.Value;
                        this.releaseDate = boxset.ReleaseDate.Value.ToString("MM-dd-yyyy");
                    }
                    if (!string.IsNullOrEmpty(boxset.Locale)) this.request.Locale = boxset.Locale;
                    if (!string.IsNullOrEmpty(boxset.RegionCode)) this.request.RegionCode = boxset.RegionCode;
                    if (!string.IsNullOrEmpty(boxset.Asin)) this.request.Asin = boxset.Asin;
                    if (!string.IsNullOrEmpty(boxset.Upc)) this.request.Upc = boxset.Upc;

                    // Copy the boxset's cover images into the new contribution's temp upload
                    // location so the user doesn't have to re-upload them. The existing
                    // CreateContribution → MoveImages flow then promotes the temp files to
                    // the contribution's permanent location on submit. Reuses the same
                    // UploadImage helper used by the Amazon-import path.
                    //
                    // Reset image state first so switching from a boxset with images to one
                    // without (or after a copy failure) doesn't leave stale URLs in the form.
                    this.request.FrontImageUrl = string.Empty;
                    this.request.BackImageUrl = string.Empty;
                    this.frontImagePreviewUrl = string.Empty;
                    this.backImagePreviewUrl = string.Empty;

                    if (!string.IsNullOrEmpty(boxset.FrontImageUrl))
                    {
                        try
                        {
                            this.request.FrontImageUrl = await UploadImage(this.id.ToString(), boxset.FrontImageUrl, this.frontImageUploadUrl, "front", this.frontImageUploader) ?? string.Empty;
                            if (!string.IsNullOrEmpty(this.request.FrontImageUrl))
                            {
                                this.frontImagePreviewUrl = $"/api/contribute/images/Contributions/releaseImages/{this.id}/front.jpg";
                            }
                        }
                        catch (Exception ex)
                        {
                            Console.WriteLine($"Failed to copy boxset front image: {ex.Message}");
                        }
                    }
                    if (!string.IsNullOrEmpty(boxset.BackImageUrl))
                    {
                        try
                        {
                            this.request.BackImageUrl = await UploadImage(this.id.ToString(), boxset.BackImageUrl, this.backImageUploadUrl, "back", this.backImageUploader);
                            if (!string.IsNullOrEmpty(this.request.BackImageUrl))
                            {
                                this.backImagePreviewUrl = $"/api/contribute/images/Contributions/releaseImages/{this.id}/back.jpg";
                            }
                        }
                        catch (Exception ex)
                        {
                            Console.WriteLine($"Failed to copy boxset back image: {ex.Message}");
                        }
                    }
                }
            }

            if (this.slugInput != null)
            {
                await this.slugInput.RecheckAvailability(this.request.ReleaseSlug);
            }
        }
    }

    private string? submitErrorMessage;

    private async Task TryApplyIntakeReleaseMatchAsync()
    {
        this.intakePrefillMessage = null;
        var normalizedUpc = string.Concat((this.form?.Upc ?? string.Empty).Where(char.IsAsciiDigit));
        if (normalizedUpc.Length is not (12 or 13) ||
            string.IsNullOrWhiteSpace(this.request.ExternalId))
        {
            return;
        }

        var response = await this.ContributionClient.GetIntakeReleaseMatch.ExecuteAsync(
            this.request.ExternalProvider,
            this.request.ExternalId,
            normalizedUpc,
            this.CancellationToken);
        var match = response.Data?.IntakeReleaseMatch;
        if (!response.IsSuccessResult() || match is null)
        {
            return;
        }

        var applied = false;
        if (!this.form!.AsinNotAvailable && string.IsNullOrWhiteSpace(this.form.Asin) && !string.IsNullOrWhiteSpace(match.Asin))
        {
            this.form.Asin = match.Asin;
            applied = true;
        }

        if (string.IsNullOrWhiteSpace(this.form.ReleaseTitle) && !string.IsNullOrWhiteSpace(match.ReleaseTitle))
        {
            this.form.ReleaseTitle = match.ReleaseTitle;
            applied = true;
        }

        if (string.IsNullOrWhiteSpace(this.form.ReleaseSlug) && !string.IsNullOrWhiteSpace(match.ReleaseSlug))
        {
            this.form.ReleaseSlug = match.ReleaseSlug;
            applied = true;
        }

        if ((string.IsNullOrWhiteSpace(this.form.Locale) ||
                this.form.Locale.Equals("en-us", StringComparison.OrdinalIgnoreCase)) &&
            !string.IsNullOrWhiteSpace(match.Locale))
        {
            this.form.Locale = match.Locale;
            applied = true;
        }

        if ((string.IsNullOrWhiteSpace(this.form.RegionCode) || this.form.RegionCode == "1") &&
            !string.IsNullOrWhiteSpace(match.RegionCode))
        {
            this.form.RegionCode = match.RegionCode;
            applied = true;
        }

        if (string.IsNullOrWhiteSpace(this.releaseDate) && match.ReleaseDate.HasValue)
        {
            this.request.ReleaseDate = match.ReleaseDate.Value;
            this.releaseDate = match.ReleaseDate.Value.ToString("MM-dd-yyyy");
            applied = true;
        }

        if (string.IsNullOrWhiteSpace(this.request.FrontImageUrl) &&
            !string.IsNullOrWhiteSpace(match.FrontImageUrl))
        {
            try
            {
                this.request.FrontImageUrl = await UploadImage(
                    this.id.ToString(),
                    match.FrontImageUrl,
                    this.frontImageUploadUrl,
                    "front",
                    this.frontImageUploader) ?? string.Empty;
                if (!string.IsNullOrWhiteSpace(this.request.FrontImageUrl))
                {
                    this.frontImagePreviewUrl = $"/api/contribute/images/Contributions/releaseImages/{this.id}/front.jpg";
                    applied = true;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Failed to copy intake front image: {ex.Message}");
            }
        }

        if (string.IsNullOrWhiteSpace(this.request.BackImageUrl) &&
            !string.IsNullOrWhiteSpace(match.BackImageUrl))
        {
            try
            {
                this.request.BackImageUrl = await UploadImage(
                    this.id.ToString(),
                    match.BackImageUrl,
                    this.backImageUploadUrl,
                    "back",
                    this.backImageUploader);
                if (!string.IsNullOrWhiteSpace(this.request.BackImageUrl))
                {
                    this.backImagePreviewUrl = $"/api/contribute/images/Contributions/releaseImages/{this.id}/back.jpg";
                    applied = true;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Failed to copy intake back image: {ex.Message}");
            }
        }

        if (this.slugInput != null && !string.IsNullOrWhiteSpace(this.form.ReleaseSlug))
        {
            await this.slugInput.RecheckAvailability(this.form.ReleaseSlug);
        }

        if (applied)
        {
            this.intakePrefillMessage = "Available release details were filled from matching intake evidence. Your entries were preserved.";
        }
    }

    async Task HandleValidSubmit()
    {
        this.submitErrorMessage = null;

        if (string.IsNullOrEmpty(this.releaseDate))
        {
            // The release date is required. TODO: Show an error message.
            this.releaseDateValidationMessage = "Release Date is required.";
            return;
        }

        bool releaseDateParsed = DateTimeOffset.TryParse(this.releaseDate, out DateTimeOffset date);
        if (!releaseDateParsed)
        {
            this.releaseDateValidationMessage = $"'{this.releaseDate}' is not a valid date.";
            return;
        }
        else
        {
            this.request.ReleaseDate = date;
        }

        if (this.form?.AsinNotAvailable == true)
        {
            this.request.Asin = string.Empty;
        }
        
        var result = await this.ContributionClient.CreateContribution.ExecuteAsync(new CreateContributionInput
        {
            Input = this.request
        });

        if (result == null || !result.IsSuccessResult())
        {
            this.submitErrorMessage = result?.Errors.FirstOrDefault()?.Message
                ?? "Failed to create the contribution. Please try again.";
            return;
        }

        // Domain errors come back via the payload `errors` union, not the transport-level errors.
        // When boxset linkage validation fails (BoxsetNotFound, InvalidOwnership,
        // InvalidBoxsetStatus, InvalidId), `userContribution` is null.
        var payload = result.Data?.CreateContribution;
        if (payload?.Errors is { Count: > 0 } domainErrors)
        {
            this.submitErrorMessage = domainErrors[0] switch
            {
                ICreateContribution_CreateContribution_Errors_AuthenticationError e => e.Message,
                ICreateContribution_CreateContribution_Errors_BoxsetNotFoundError e => e.Message,
                ICreateContribution_CreateContribution_Errors_InvalidIdError e => e.Message,
                ICreateContribution_CreateContribution_Errors_InvalidOwnershipError e => e.Message,
                ICreateContribution_CreateContribution_Errors_InvalidBoxsetStatusError e => e.Message,
                _ => "An unexpected error occurred while creating the contribution."
            };
            return;
        }

        var newContribution = payload?.UserContribution;
        if (newContribution == null)
        {
            this.submitErrorMessage = "The contribution was not created. Please try again.";
            return;
        }

        if (this.request.MediaType.Equals("series", StringComparison.OrdinalIgnoreCase))
        {
            // Create the episode names for later use
            var episodeResponse = await this.ContributionClient.GetEpisodeNames.ExecuteAsync(new EpisodeNamesInput
            {
                ContributionId = newContribution.EncodedId!
            });

            if (episodeResponse == null || !episodeResponse.IsSuccessResult())
            {
                // TODO: Show an error message
                var error = episodeResponse?.Errors.FirstOrDefault();
                Console.WriteLine("Failed to create episode names. " + error?.Message);
            }
        }
        this.NavigationManager!.NavigateTo($"/contribution/{newContribution.EncodedId!}");
    }

    private void OnAsinInput(ChangeEventArgs args)
    {
        if (this.form?.AsinNotAvailable == true)
        {
            this.request.Asin = string.Empty;
            return;
        }

        this.request.Asin = args.Value?.ToString() ?? string.Empty;
    }

    private void OnAsinAvailabilityChanged()
    {
        if (this.form?.AsinNotAvailable == true)
        {
            this.form.Asin = null;
            this.request.Asin = string.Empty;
        }
    }

    private async Task ReleaseTitleChanged(ChangeEventArgs args)
    {
        if (args?.Value != null)
        {
            string title = args.Value.ToString()!;
            this.form!.ReleaseTitle = title;

            this.request.ReleaseSlug = HttpUtility.UrlEncode(CreateSlug(title, GetReleaseSlugYear(title)));
            if (this.slugInput != null)
            {
                await this.slugInput.RecheckAvailability(this.request.ReleaseSlug);
            }
        }
    }

    private async Task ApplyReleaseNamingSuggestion()
    {
        var suggestion = this.ReleaseNamingSuggestion;
        if (suggestion?.CanApply != true)
        {
            return;
        }

        this.form!.ReleaseTitle = suggestion.SuggestedName;
        this.form.ReleaseSlug = suggestion.SuggestedSlug;
        if (this.slugInput != null)
        {
            await this.slugInput.RecheckAvailability(this.form.ReleaseSlug);
        }
    }

    private int? GetReleaseSlugYear(string title)
    {
        int releaseYear = this.request.ReleaseDate.Year;
        return releaseYear > 1980 && !title.Contains(releaseYear.ToString(), StringComparison.Ordinal)
            ? releaseYear
            : null;
    }

    private async Task ReleaseDateChanged(ChangeEventArgs args)
    {
        if (args?.Value != null)
        {
            // First, try the format on Amazon
            if (DateTimeOffset.TryParseExact(args.Value.ToString(), "MMMM d, yyyy", null, System.Globalization.DateTimeStyles.None, out var parsedDate))
            {
                this.request.ReleaseDate = parsedDate;
                this.releaseDate = parsedDate.ToString("MM-dd-yyyy");
                if (!string.IsNullOrEmpty(request.ReleaseTitle))
                {
                    int? year = null;
                    if (this.request.ReleaseDate.Year > 1980)
                    {
                        year = this.request.ReleaseDate.Year;
                    }

                    this.request.ReleaseSlug = CreateSlug(request.ReleaseTitle, year);
                }
                else
                {
                    this.request.ReleaseSlug = "";
                }
            }
            else
            {
                this.releaseDate = args.Value.ToString() ?? ""; // just set the value to be validated on submit
                this.request.ReleaseDate = DateTimeOffset.MinValue;
            }
        }
        else
        {
            this.releaseDate = string.Empty;
            this.request.ReleaseDate = DateTimeOffset.MinValue;
            if (!string.IsNullOrEmpty(request.ReleaseTitle))
            {
                this.request.ReleaseSlug = CreateSlug(request.ReleaseTitle, year: null);
            }
            else
            {
                this.request.ReleaseSlug = "";
            }
        }

        if (this.slugInput != null)
        {
            await this.slugInput.RecheckAvailability(this.request.ReleaseSlug);
        }
    }

    private static string CreateSlug(string title, int? year)
    {
        string slug = title.Slugify();

        if (year.HasValue)
        {
            slug = $"{year.Value}-{slug}";
        }

        return slug;
    }

    private void FrontImageSelected(SelectedEventArgs args)
    {
        this.request.FrontImageUrl = $"{this.id}/front.jpg";
    }

    private void FrontImageUploadSuccess(SuccessEventArgs args)
    {
        this.frontImagePreviewUrl = $"/api/contribute/images/Contributions/releaseImages/{this.id}/front.jpg";
    }

    private void FrontImageRemoved(RemovingEventArgs args)
    {
        this.request.FrontImageUrl = "";
        this.frontImagePreviewUrl = "";
    }

    private void BackImageSelected(SelectedEventArgs args)
    {
        this.request.BackImageUrl = $"{this.id}/back.jpg";
    }

    private void BackImageUploadSuccess(SuccessEventArgs args)
    {
        this.backImagePreviewUrl = $"/api/contribute/images/Contributions/releaseImages/{this.id}/back.jpg";
    }

    private void BackImageRemoved(RemovingEventArgs args)
    {
        this.request.BackImageUrl = "";
        this.backImagePreviewUrl = "";
    }

    private async Task ImportFromAmazon(Microsoft.AspNetCore.Components.Web.MouseEventArgs args)
    {
        if (string.IsNullOrEmpty(this.request.Asin))
        {
            return;
        }

        IsAmazonImportInProgress = true;

        try
        {
            var response = await this.ContributionClient.GetAmazonProductMetadata.ExecuteAsync(this.request.Asin);
            if (response == null || !response.IsSuccessResult() || response.Data?.AmazonProductMetadata == null)
            {
                toastContent = "Unable to import from Amazon. Details must be entered manually.";
                await toast!.ShowAsync();
                return;
            }

            var details = response.Data.AmazonProductMetadata;
            this.request.Title = details.Title ?? this.request.Title;
            if (!string.IsNullOrEmpty(details.Upc))
            {
                this.request.Upc = details.Upc;
            }

            if (details.ReleaseDate.HasValue)
            {
                this.request.ReleaseDate = details.ReleaseDate.Value;
                this.releaseDate = details.ReleaseDate.Value.ToString("MM-dd-yyyy");

                if (!string.IsNullOrEmpty(details.MediaFormat) && string.IsNullOrEmpty(this.request.ReleaseTitle))
                {
                    this.request.ReleaseTitle = $"{details.ReleaseDate.Value.Year} {details.MediaFormat}";
                    this.request.ReleaseSlug = this.request.ReleaseTitle.Slugify();
                    if (this.slugInput != null)
                    {
                        await this.slugInput.RecheckAvailability(this.request.ReleaseSlug);
                    }
                }
            }

            if (!string.IsNullOrEmpty(details.FrontImageUrl))
            {
                try
                {
                    request.FrontImageUrl = await UploadImage(this.id.ToString(), details.FrontImageUrl, this.frontImageUploadUrl, "front", frontImageUploader) ?? string.Empty;
                    this.frontImagePreviewUrl = $"/api/contribute/images/Contributions/releaseImages/{id}/front.jpg";
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Failed to upload front image: {ex.Message}");
                }
            }

            if (!string.IsNullOrEmpty(details.BackImageUrl))
            {
                try
                {
                    request.BackImageUrl = await UploadImage(this.id.ToString(), details.BackImageUrl, this.backImageUploadUrl, "back", backImageUploader);
                    this.backImagePreviewUrl = $"/api/contribute/images/Contributions/releaseImages/{id}/back.jpg";
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Failed to upload back image: {ex.Message}");
                }
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine("Failed to import release details: " + ex.Message);
            toastContent = "Unable to import from Amazon. Details must be entered manually.";
            await toast!.ShowAsync();
        }
        finally
        {
            IsAmazonImportInProgress = false;
        }
    }

    private async Task<string?> UploadImage(string id, string url, string uploadUrl, string name, SfUploader? uploader)
    {
        using var downloadResponse = await this.HttpClient.GetAsync(url);
        downloadResponse.EnsureSuccessStatusCode();
        var data = await downloadResponse.Content.ReadAsByteArrayAsync();
        var contentType = downloadResponse.Content.Headers.ContentType?.MediaType ?? "image/jpeg";
        var extension = contentType.ToLowerInvariant() switch
        {
            "image/png" => ".png",
            "image/webp" => ".webp",
            _ => ".jpg",
        };
        var fileName = $"{name}{extension}";
        var imageContent = new ByteArrayContent(data);
        imageContent.Headers.ContentType =
            System.Net.Http.Headers.MediaTypeHeaderValue.Parse(contentType);
        var content = new MultipartFormDataContent
        {
            { imageContent, name, fileName }
        };

        var uploadResponse = await this.HttpClient.PostAsync(uploadUrl, content);
        if (uploadResponse != null && uploadResponse.IsSuccessStatusCode)
        {
            if (uploader != null)
            {
                await uploader.CreateFileList(
                [
                    new Syncfusion.Blazor.Inputs.FileInfo
                    {
                        Id = id,
                        Name = fileName,
                        Size = data.Length,
                        Type = contentType,
                        StatusCode = "Uploaded",
                        Status = "File uploaded successfully",
                        LastModifiedDate = DateTime.UtcNow
                    }
                ]);
            }

            return $"{this.id}/{name}.jpg";
        }
        else
        {
            Console.WriteLine("Failed to upload image " + uploadResponse?.StatusCode);
        }

        return null;
    }

    private async Task BeforeFrontImageRemove(BeforeRemoveEventArgs args)
    {
        if (frontImageUploader != null)
        {
            await frontImageUploader.ClearAllAsync();
            await this.HttpClient.PostAsync(this.frontImageRemoveUrl, null);
            this.request.FrontImageUrl = "";
            this.frontImagePreviewUrl = "";
        }
    }

    private async Task BeforeBackImageRemove(BeforeRemoveEventArgs args)
    {
        if (backImageUploader != null)
        {
            await backImageUploader.ClearAllAsync();
            await this.HttpClient.PostAsync(this.backImageRemoveUrl, null);
            this.request.BackImageUrl = null;
            this.backImagePreviewUrl = "";
        }
    }

    private async Task<bool> CheckReleaseSlugAvailability(string slug, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(this.ExternalId))
        {
            return true;
        }

        var result = await this.TheDiscDbClient.CheckReleaseSlugAvailability.ExecuteAsync(
            this.ExternalId,
            slug,
            cancellationToken);

        if (result?.Data?.MediaItems?.Nodes is { Count: > 0 })
        {
            return false;
        }

        return true;
    }

}
