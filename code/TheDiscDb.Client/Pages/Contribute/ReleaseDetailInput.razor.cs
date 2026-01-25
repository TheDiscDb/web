using System.Reflection.Metadata;
using System.Web;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Components;
using StrawberryShake;
using Syncfusion.Blazor.Inputs;
using Syncfusion.Blazor.Notifications;
using TheDiscDb.Client.Contributions;

namespace TheDiscDb.Client.Pages.Contribute;

[Authorize]
public partial class ReleaseDetailInput : ComponentBase
{
    [Parameter]
    public string? MediaType { get; set; }

    [Parameter]
    public string? ExternalId { get; set; }

    [Inject]
    public IContributionClient ContributionClient { get; set; } = default!;

    [Inject]
    public NavigationManager NavigationManager { get; set; } = default!;

    [Inject]
    public HttpClient HttpClient { get; set; } = default!;

    private readonly ContributionMutationRequestInput request = new ContributionMutationRequestInput
    {
        Locale = "en-us",
        RegionCode = "1",
        Status = UserContributionStatus.Pending
    };

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
    private string BreadcrumbText => $"{this.externalData!.Title} ({this.externalData!.Year}) Details";
    private bool ImportFromAmazonDisabled => string.IsNullOrEmpty(this.request.Asin);
    private bool IsAmazonImportInProgress = false;

    private SfUploader? frontImageUploader;
    private SfUploader? backImageUploader;
    SfToast? toast;
#pragma warning disable IDE0044 // Add readonly modifier
    string? toastContent = "Test";
#pragma warning restore IDE0044 // Add readonly modifier

    protected override async Task OnInitializedAsync()
    {
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

    async Task HandleValidSubmit()
    {
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
        
        var result = await this.ContributionClient.CreateContribution.ExecuteAsync(new CreateContributionInput
        {
            Input = this.request
        });

        if (result != null && result.IsSuccessResult())
        {
            if (this.request.MediaType.Equals("series", StringComparison.OrdinalIgnoreCase))
            {
                // Create the episode names for later use
                var episodeResponse = await this.ContributionClient.GetEpisodeNames.ExecuteAsync(new EpisodeNamesInput
                {
                    ContributionId = result.Data!.CreateContribution.UserContribution!.EncodedId!
                });

                if (episodeResponse == null || !episodeResponse.IsSuccessResult())
                {
                    // TODO: Show an error message
                    var error = episodeResponse?.Errors.FirstOrDefault();
                    Console.WriteLine("Failed to create episode names. " + error?.Message);
                }
            }
            this.NavigationManager!.NavigateTo($"/contribution/{result.Data!.CreateContribution.UserContribution!.EncodedId!}");
        }
    }

    private void ReleaseTitleChanged(ChangeEventArgs args)
    {
        if (args?.Value != null)
        {
            string title = args.Value.ToString()!;
            int? year = null;
            if (this.request.ReleaseDate.Year > 1980 && !title.Contains(this.request.ReleaseDate.Year.ToString()))
            {
                year = this.request.ReleaseDate.Year;
            }

            this.request.ReleaseSlug = HttpUtility.UrlEncode(CreateSlug(title, year));
        }
    }

    private void ReleaseDateChanged(ChangeEventArgs args)
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

    private void FrontImageRemoved(RemovingEventArgs args)
    {
        this.request.FrontImageUrl = "";
        this.frontImagePreviewUrl = "";
    }

    private void BackImageSelected(SelectedEventArgs args)
    {
        this.request.BackImageUrl = $"{this.id}/back.jpg";
    }

    private void BackImageRemoved(RemovingEventArgs args)
    {
        this.request.BackImageUrl = "";
        this.backImagePreviewUrl = "";
    }

    //private async Task ImportFromAmazon(Microsoft.AspNetCore.Components.Web.MouseEventArgs args)
    //{
    //    if (string.IsNullOrEmpty(this.request.Asin))
    //    {
    //        return;
    //    }

    //    IsAmazonImportInProgress = true;

    //    var response = await this.Client.ImportReleaseDetails(this.request.Asin);
    //    if (response == null || response.IsFailed)
    //    {
    //        foreach (var error in response?.Errors ?? [])
    //        {
    //            Console.WriteLine("Failed to import release details " + error.Message);
    //            toastContent = "Unable to import from Amazon. Details must be entered manually.";
    //            await toast!.ShowAsync();
    //        }
    //        return;
    //    }

    //    var details = response.Value;
    //    this.request.Title = details.Title ?? "";
    //    this.request.RegionCode = details.RegionCode ?? "1";
    //    this.request.Locale = details.Locale ?? "en-us";
    //    this.request.Upc = details.Upc ?? "";

    //    if (details.ReleaseDate.HasValue)
    //    {
    //        this.request.ReleaseDate = details.ReleaseDate.Value;
    //        this.releaseDate = details.ReleaseDate.Value.ToString("MM-dd-yyyy");

    //        if (!string.IsNullOrEmpty(details.MediaFormat) && string.IsNullOrEmpty(this.request.ReleaseTitle))
    //        {
    //            this.request.ReleaseTitle = $"{details.ReleaseDate.Value.Year} {details.MediaFormat}";
    //            this.request.ReleaseSlug = this.request.ReleaseTitle.Slugify();
    //        }
    //    }

    //    if (!string.IsNullOrEmpty(details.FrontImageUrl))
    //    {
    //        request.FrontImageUrl = await UploadImage(this.id.ToString(), details.FrontImageUrl, this.frontImageUploadUrl, "front", frontImageUploader);
    //        this.frontImagePreviewUrl = $"/images/Contributions/releaseImages/{id}/front.jpg?width=156&height=231";
    //    }

    //    if (!string.IsNullOrEmpty(details.BackImageUrl))
    //    {
    //        request.BackImageUrl = await UploadImage(this.id.ToString(), details.BackImageUrl, this.backImageUploadUrl, "back", backImageUploader);
    //        this.backImagePreviewUrl = $"/images/Contributions/releaseImages/{id}/back.jpg?width=156&height=231";
    //    }

    //    IsAmazonImportInProgress = false;
    //}

    private async Task<string?> UploadImage(string id, string url, string uploadUrl, string name, SfUploader? uploader)
    {
        var data = await this.HttpClient.GetByteArrayAsync(url);
        var content = new MultipartFormDataContent
        {
            { new ByteArrayContent(data), name, $"{name}.jpg" }
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
                        Name = $"{name}.jpg",
                        Size = data.Length,
                        Type = "image/jpeg",
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

}
