using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Components;
using Syncfusion.Blazor.Inputs;
using TheDiscDb.Services;

namespace TheDiscDb.Client.Pages.Contribute;

[Authorize]
public partial class ReleaseDetailInput : ComponentBase
{
    [Parameter]
    public string? MediaType { get; set; }

    [Parameter]
    public string? ExternalId { get; set; }

    [Inject]
    public IUserContributionService Client { get; set; } = default!;

    [Inject]
    public NavigationManager NavigationManager { get; set; } = default!;

    [Inject]
    public HttpClient HttpClient { get; set; } = default!;

    private readonly CreateContributionRequest request = new CreateContributionRequest
    {
        Locale = "en-us",
        RegionCode = "1"
    };

    private string releaseDate = string.Empty;
    private string releaseDateValidationMessage = string.Empty;
    private ExternalMetadata? externalData;
    private readonly Guid id = Guid.NewGuid();
    private string frontImageUploadUrl => $"/api/contribute/images/front/upload/{id}";
    private string frontImageRemoveUrl => $"/api/contribute/images/front/remove/{id}";
    private string backImageUploadUrl => $"/api/contribute/images/back/upload/{id}";
    private string backImageRemoveUrl => $"/api/contribute/images/back/remove/{id}";
    private string BreadcrumbText => $"{this.externalData!.Title} ({this.externalData!.Year}) Details";
    private bool ImportFromAmazonDisabled => string.IsNullOrEmpty(this.request.Asin);
    private SfUploader? frontImageUploader;
    private SfUploader? backImageUploader;

    protected override async Task OnInitializedAsync()
    {
        this.request.MediaType = this.MediaType ?? "Movie";
        this.request.ExternalProvider = "TMDB";
        this.request.ExternalId = this.ExternalId ?? string.Empty;

        var response = await this.Client.GetExternalData(this.request.ExternalId, this.request.MediaType, this.request.ExternalProvider);
        if (response != null && response.IsSuccess)
        {
            this.externalData = response.Value;
        }
    }

    async Task HandleValidSubmit()
    {
        if (string.IsNullOrEmpty(this.releaseDate))
        {
            // The release date is required. TODO: Show an error message.
            this.releaseDateValidationMessage = "Release Date is required.";
            return;
        }

        var response = await this.Client.CreateContribution(userId: string.Empty, this.request);

        if (this.request.MediaType.Equals("series", StringComparison.OrdinalIgnoreCase))
        {
            // Create the episode names for later use
            var episodeResponse = await this.Client.GetEpisodeNames(response.Value.ContributionId);
            if (episodeResponse == null || episodeResponse.IsFailed)
            {
                // TODO: Show an error message
                var error = episodeResponse?.Errors.FirstOrDefault();
                Console.WriteLine("Failed to create episode names. " + error?.Message);
            }
        }

        this.NavigationManager!.NavigateTo($"/contribution/{response.Value.ContributionId}");
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

            this.request.ReleaseSlug = CreateSlug(title, year);
        }
    }

    private void ReleaseDateChanged(ChangeEventArgs args)
    {
        if (args?.Value != null)
        {
            if (DateTimeOffset.TryParse(args.Value.ToString(), out var date))
            {
                this.request.ReleaseDate = date;
                this.releaseDate = date.ToString("MM-dd-yyyy");

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
        this.request.FrontImageUrl = null;
        this.frontImagePreviewUrl = "";
    }

    private void BackImageSelected(SelectedEventArgs args)
    {
        this.request.BackImageUrl = $"{this.id}/back.jpg";
    }

    private void BackImageRemoved(RemovingEventArgs args)
    {
        this.request.BackImageUrl = null;
        this.backImagePreviewUrl = "";
    }

    private async Task ImportFromAmazon(Microsoft.AspNetCore.Components.Web.MouseEventArgs args)
    {
        if (string.IsNullOrEmpty(this.request.Asin))
        {
            return;
        }

        var response = await this.Client.ImportReleaseDetails(this.request.Asin);
        if (response == null || response.IsFailed)
        {
            foreach (var error in response?.Errors ?? [])
            {
                Console.WriteLine("Failed to import release details " + error.Message);
            }
            return;
        }

        var details = response.Value;
        this.request.Title = details.Title ?? "";
        this.request.RegionCode = details.RegionCode ?? "1";
        this.request.Locale = details.Locale ?? "en-us";
        this.request.Upc = details.Upc ?? "";

        if (details.ReleaseDate.HasValue)
        {
            this.request.ReleaseDate = details.ReleaseDate.Value;
            this.releaseDate = details.ReleaseDate.Value.ToString("MM-dd-yyyy");

            if (!string.IsNullOrEmpty(details.MediaFormat) && string.IsNullOrEmpty(this.request.ReleaseTitle))
            {
                this.request.ReleaseTitle = $"{details.ReleaseDate.Value.Year} {details.MediaFormat}";
                this.request.ReleaseSlug = this.request.ReleaseTitle.Slugify();
            }
        }

        if (!string.IsNullOrEmpty(details.FrontImageUrl))
        {
            await UploadImage(this.id.ToString(), details.FrontImageUrl, this.frontImageUploadUrl, "front", frontImageUploader);
            this.frontImagePreviewUrl = $"/images/Contributions/releaseImages/{id}/front.jpg?width=156&height=231";
        }

        if (!string.IsNullOrEmpty(details.BackImageUrl))
        {
            await UploadImage(this.id.ToString(), details.BackImageUrl, this.backImageUploadUrl, "back", backImageUploader);
            this.backImagePreviewUrl = $"/images/Contributions/releaseImages/{id}/back.jpg?width=156&height=231";
        }
    }

    string frontImagePreviewUrl = "";
    string backImagePreviewUrl = "";

    private async Task UploadImage(string id, string url, string uploadUrl, string name, SfUploader? uploader)
    {
        var data = await this.HttpClient.GetByteArrayAsync(url);
        var content = new MultipartFormDataContent
            {
                { new ByteArrayContent(data), name, $"{name}.jpg" }
            };
        var uploadResponse = await this.HttpClient.PostAsync(this.frontImageUploadUrl, content);
        if (uploadResponse != null && uploadResponse.IsSuccessStatusCode)
        {
            this.request.FrontImageUrl = $"{this.id}/{name}.jpg";

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
        }
        else
        {
            Console.WriteLine("Failed to upload image " + uploadResponse?.StatusCode);
        }
    }
}
