namespace TheDiscDb.Client.Pages.Contribute;

using System.Text;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;
using StrawberryShake;
using Syncfusion.Blazor.Inputs;
using TheDiscDb.Client.Contributions;
using TheDiscDb.Client.Services;

/// <summary>
/// Shown after a MakeMKV log upload to offer an optional disc scan before identification.
/// </summary>
[Authorize]
public partial class DiscScanOffer : CancellableComponentBase
{
    [Parameter]
    public string? ContributionId { get; set; }

    [Parameter]
    public string? DiscId { get; set; }

    [Inject]
    private IContributionClient ContributionClient { get; set; } = default!;

    [Inject]
    private DiscScanUploader ScanUploader { get; set; } = default!;

    [Inject]
    private HttpClient HttpClient { get; set; } = default!;

    [Inject]
    private NavigationManager NavigationManager { get; set; } = default!;

    [Inject]
    private IJSRuntime JSRuntime { get; set; } = default!;

    [Inject]
    private IConfiguration Configuration { get; set; } = default!;

    // Hidden at launch; set Contributions:ShowScanFileUpload to offer the thediscdb-scan file upload.
    private bool ShowScanFileUpload => this.Configuration.GetValue<bool>("Contributions:ShowScanFileUpload");

    private IDiscUploadPageData_MyContributions_Nodes? contribution;
    private IDiscUploadPageData_MyContributions_Nodes_Discs? disc;
    private bool isLoading = true;
    private bool isScanning;
    private bool isUploadingFile;
    private bool scanUploaded;
    private string? scanProgress;
    private string? error;
    private IReadOnlyList<string> scanWarnings = [];

    private bool isBusy => this.isScanning || this.isUploadingFile;

    private string ManifestUri => $"{this.NavigationManager.BaseUri}api/contribute/{this.ContributionId}/discs/{this.DiscId}/manifest";

    protected override async Task OnInitializedAsync()
    {
        var response = await this.ContributionClient.DiscUploadPageData.ExecuteAsync(this.ContributionId ?? string.Empty, this.CancellationToken);
        if (response != null && response.IsSuccessResult())
        {
            this.contribution = response.Data!.MyContributions!.Nodes!.FirstOrDefault();
            this.disc = this.contribution?.Discs.FirstOrDefault(d => d.EncodedId == this.DiscId);
        }

        this.isLoading = false;

        if (this.disc is not null && !this.disc.LogsUploaded)
        {
            this.NavigationManager.NavigateTo($"/contribution/{this.ContributionId}/discs/{this.DiscId}", replace: true);
        }
        else if (this.disc?.ManifestUploaded == true)
        {
            NavigateToIdentify();
        }
    }

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (firstRender)
        {
            await this.ScanUploader.PreloadAsync(this.CancellationToken);
        }
    }

    private async Task ScanDiscAsync()
    {
        this.error = null;
        this.scanWarnings = [];
        this.scanProgress = null;

        try
        {
            var result = await this.ScanUploader.ScanAndUploadAsync(
                this.ManifestUri,
                MarkScanning,
                ReportScanProgress,
                this.CancellationToken);

            this.scanWarnings = result.Warnings;
            this.error = result.Error;
            if (result.Uploaded)
            {
                OnManifestUploaded();
            }
        }
        finally
        {
            this.isScanning = false;
            this.scanProgress = null;
            this.StateHasChanged();
        }
    }

    private async Task ManifestFileChange(UploadChangeEventArgs args)
    {
        var file = args.Files.FirstOrDefault();
        if (file is null)
        {
            return;
        }

        this.error = null;
        this.scanWarnings = [];
        this.isUploadingFile = true;
        try
        {
            using var stream = file.File.OpenReadStream(long.MaxValue);
            using var reader = new StreamReader(stream);
            string contents = await reader.ReadToEndAsync(this.CancellationToken);
            using var content = new StringContent(contents, Encoding.UTF8, "application/json");
            var response = await this.HttpClient.PostAsync(this.ManifestUri, content, this.CancellationToken);
            if (response.IsSuccessStatusCode)
            {
                OnManifestUploaded();
            }
            else
            {
                string body = await response.Content.ReadAsStringAsync(this.CancellationToken);
                this.error = DiscScanUploader.ExtractErrorDetail(body, "scan file");
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine(ex);
            this.error = "An unexpected error occurred uploading the scan file.";
        }
        finally
        {
            this.isUploadingFile = false;
            this.StateHasChanged();
        }
    }

    // Warnings are worth a look before moving on; a clean upload goes straight to identification.
    private void OnManifestUploaded()
    {
        if (this.scanWarnings.Count == 0)
        {
            NavigateToIdentify();
        }
        else
        {
            this.scanUploaded = true;
        }
    }

    private void NavigateToIdentify()
        => this.JSRuntime.InvokeVoidAsync("window.location.replace", $"/contribution/{this.ContributionId}/discs/{this.DiscId}/identify");

    private void MarkScanning()
    {
        this.isScanning = true;
        this.StateHasChanged();
    }

    private void ReportScanProgress(string message)
    {
        this.scanProgress = message;
        this.InvokeAsync(this.StateHasChanged);
    }
}
