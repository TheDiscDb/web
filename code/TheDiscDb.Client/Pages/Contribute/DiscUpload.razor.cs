using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;
using StrawberryShake;
using Syncfusion.Blazor.Inputs;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using TheDiscDb.Client.Contributions;
using TheDiscDb.Client.Interop;
using TheDiscDb.Client.Services;
using TheDiscDb.OpticalDiscManifest.Models;

namespace TheDiscDb.Client.Pages.Contribute;

record State(string Text, string IconClassName, bool IsDisabled = false);

[Authorize]
public partial class DiscUpload : CancellableComponentBase
{
    [Parameter]
    public string? ContributionId { get; set; }

    [Parameter]
    public string? DiscId { get; set; }

    [Inject]
    private NavigationManager NavigationManager { get; set; } = null!;

    [Inject]
    private IJSRuntime JSRuntime { get; set; } = null!;

    [Inject]
    private IClipboardService Clipboard { get; set; } = null!;

    [Inject]
    public IContributionClient ContributionClient { get; set; } = default!;

    [Inject]
    private HttpClient HttpClient { get; set; } = null!;

    [Inject]
    private DiscDirectoryPicker DiscDirectoryPicker { get; set; } = default!;

    [Inject]
    private BrowserOpticalDiscManifestScanner ManifestScanner { get; set; } = default!;

    [Inject]
    private IConfiguration Configuration { get; set; } = null!;

    [SupplyParameterFromQuery(Name = "scan")]
    public string? ScanQuery { get; set; }

    private readonly string powershellCommandTemplate = "Invoke-WebRequest -Uri \"{0}\" -Method POST -UseBasicParsing -ContentType \"text/plain\" -Body ((& '{1}' --minlength=0 --robot info disc:{2}) | Out-String)";
    private readonly string bashCommandTemplate = "makemkvcon --minlength=0 --robot info disc:{1} 2>&1 | curl -X POST -H \"Content-Type: text/plain\" --data-binary @- {0}";
    //private readonly string powershellLocalCommandTempalte = "Invoke-WebRequest -Uri \"{0}\" -Method POST -ContentType \"text/plain\" -Body ((Get-Content -Path '{1}') | Out-String)";

    State state = new("Copy", "e-icons e-copy");
    IDiscUploadPageData_MyContributions_Nodes? contribution;

    public string? PowershellCommand => string.Format(powershellCommandTemplate, GetUri(), GetMakeMkvPath(), this.DriveIndex);
    public string? BashCommand => string.Format(bashCommandTemplate, GetUri(), this.DriveIndex);

    public string DriveIndex { get; set; } = "0";

    int selectedIndex = 0;
    private readonly string[] driveIndices = Enumerable.Range(0, 8).Select(i => i.ToString()).ToArray();

    // Only the PowerShell and Bash tabs produce a command, so the copy button and the drive
    // selector that feeds it have nothing to act on anywhere else.
    private bool IsCommandTab => !IsDefaultMode && selectedIndex is PowershellTabIndex or BashTabIndex;

    // Scanning reads the disc in the browser and reports its own progress, so the banner that
    // waits on an upload from somewhere else would only compete with it. Polling continues
    // underneath either way, which is what carries a finished scan on to the next page.
    private bool IsScanTab => IsDefaultMode ? selectedIndex == 0 : selectedIndex == ScanTabIndex;

    private const int PowershellTabIndex = 0;
    private const int BashTabIndex = 1;
    private const int ScanTabIndex = 4;

    private string GetUri() => $"{NavigationManager.BaseUri}api/contribute/{ContributionId}/discs/{DiscId}/logs";
    private string GetManifestUri() => $"{NavigationManager.BaseUri}api/contribute/{ContributionId}/discs/{DiscId}/manifest";
    private string GetClearErrorUri() => $"{GetUri()}/error";

    private string GetMakeMkvPath() => "C:\\Program Files (x86)\\MakeMKV\\makemkvcon64.exe";

    private Timer? startSpinnerTimer;
    private Timer? pollUploadedTimer;

    private bool showSpinner;
    private string? uploadError;

    private bool isScanning;
    private string? scanProgress;
    private IReadOnlyList<string> scanWarnings = [];
    private bool showOptionalScanPanel;
    private bool showContinueToIdentify;
    private bool scanUploadSucceeded;
    private string? scanNotice;

    private bool IsDefaultMode => string.Equals(
        Configuration.GetValue<string>("Contributions:DiscScanMode"),
        "Default",
        StringComparison.OrdinalIgnoreCase);

    private IDiscUploadPageData_MyContributions_Nodes_Discs? CurrentDisc =>
        this.contribution?.Discs.FirstOrDefault(d => d.EncodedId == this.DiscId);

    private bool IsOptionalScanPanelActive => showOptionalScanPanel && !IsDefaultMode;

    protected override async Task OnInitializedAsync()
    {
        if (IsDefaultMode)
        {
            this.selectedIndex = 0;
        }

        this.startSpinnerTimer = new Timer(SpinnerTimerTick!, null, 4000, Timeout.Infinite);
        
        var response = await this.ContributionClient.DiscUploadPageData.ExecuteAsync(this.ContributionId ?? string.Empty, this.CancellationToken);
        if (response != null && response.IsSuccessResult())
        {
            this.contribution = response.Data!.MyContributions!.Nodes!.FirstOrDefault();
            var disc = CurrentDisc;
            if (!IsDefaultMode
                && ScanQuery == "1"
                && disc?.LogsUploaded == true
                && disc.ManifestUploaded == false)
            {
                ShowOptionalScanPanel();
            }
            else if (!IsDefaultMode && ScanQuery == "1")
            {
                this.selectedIndex = ScanTabIndex;
            }
        }
    }

    private void SpinnerTimerTick(object state)
    {
        this.showSpinner = true;
        this.startSpinnerTimer?.Dispose();
        InvokeAsync(StateHasChanged);

        this.pollUploadedTimer = new Timer(PollTimerTick!, null, 0, 2000);
    }

    private void PollTimerTick(object state)
    {
        var input = new DiscUploadStatusInput
        {
            DiscId = this.DiscId ?? string.Empty
        };

        this.ContributionClient.GetDiscUploadStatus.ExecuteAsync(input, this.CancellationToken).ContinueWith(t =>
        {
            if (t == null || t.IsFaulted)
            {
                return;
            }

            var status = t.Result?.Data?.DiscUploadStatus?.DiscUploadStatus;
            if (status == null)
            {
                return;
            }

            if (status.LogsUploaded)
            {
                InvokeAsync(() =>
                {
                    this.pollUploadedTimer?.Dispose();
                    if (!IsDefaultMode && !status.ManifestUploaded)
                    {
                        ShowOptionalScanPanel();
                    }
                    else
                    {
                        NavigateToIdentify();
                    }
                });
            }
            else if (!string.IsNullOrEmpty(status.LogUploadError))
            {
                InvokeAsync(() =>
                {
                    this.pollUploadedTimer?.Dispose();
                    this.uploadError = status.LogUploadError;
                    this.showSpinner = false;
                    StateHasChanged();
                });
            }
        });
    }

    private async Task CopyTextToClipboard()
    {
        string currentCommand = selectedIndex switch
        {
            BashTabIndex => this.BashCommand ?? string.Empty,
            _ => this.PowershellCommand ?? string.Empty,
        };

        if (!string.IsNullOrEmpty(currentCommand))
        {
            var temp = state;
            state = new("Copied", "e-icons e-circle-check", IsDisabled: true);
            await Clipboard.WriteTextAsync(currentCommand);
            await Task.Delay(TimeSpan.FromSeconds(2), this.CancellationToken);
            state = temp;
        }
    }

    private Task ValueChange(UploadChangeEventArgs args)
        => Upload(args, GetUri(), "text/plain", "log file");

    private Task ManifestValueChange(UploadChangeEventArgs args)
        => Upload(args, GetManifestUri(), "application/json", "disc manifest");

    private async Task CopyCommandToClipboard(string command)
    {
        if (!string.IsNullOrEmpty(command))
        {
            await Clipboard.WriteTextAsync(command);
        }
    }

    private Task CopyPowerShellCommand() => CopyCommandToClipboard(PowershellCommand ?? string.Empty);

    private Task CopyBashCommand() => CopyCommandToClipboard(BashCommand ?? string.Empty);

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (firstRender)
        {
            // Warm the interop module up front. An awaited import inside the click handler yields
            // the event loop and drops the browser's transient user activation, which makes the
            // directory picker silently refuse to open.
            await this.DiscDirectoryPicker.PreloadAsync(this.CancellationToken);
        }
    }

    private async Task ScanDiscAsync()
    {
        this.uploadError = null;
        this.scanWarnings = [];
        this.scanProgress = null;
        this.scanNotice = null;
        this.scanUploadSucceeded = false;
        this.showContinueToIdentify = false;

        try
        {
            await using var selection = await this.DiscDirectoryPicker.PickAsync(
                this.CancellationToken,
                onSelectionCommitted: MarkScanning);
            if (selection is null)
            {
                return;
            }

            // Let the busy state paint before the enumeration blocks the renderer.
            await Task.Yield();

            ManifestGenerationResult result = await this.ManifestScanner.ScanAsync(
                selection.Files,
                ReportScanProgress,
                this.CancellationToken);

            if (!result.Validation.IsValid)
            {
                // The generator validates against the same schema bytes the upload endpoint
                // enforces, so posting this would only fail again server-side.
                this.uploadError =
                    "The scan produced a manifest that does not satisfy the disc manifest schema: "
                    + string.Join(" ", result.Validation.Errors.Take(3));
                AllowIdentifyAfterOptionalScanFailure();
                return;
            }

            this.scanWarnings = result.Diagnostics
                .Where(diagnostic => diagnostic.Severity is "error" or "warning")
                .Select(diagnostic => diagnostic.Message)
                .Distinct(StringComparer.Ordinal)
                .Take(10)
                .ToArray();

            this.scanProgress = "Uploading disc manifest";
            this.StateHasChanged();

            using var content = new ByteArrayContent(result.Json);
            content.Headers.ContentType = new MediaTypeHeaderValue("application/json");
            var response = await this.HttpClient.PostAsync(
                GetManifestUri(),
                content,
                this.CancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                string body = await response.Content.ReadAsStringAsync(this.CancellationToken);
                this.uploadError = ExtractErrorDetail(body, "disc manifest");
                AllowIdentifyAfterOptionalScanFailure();
                return;
            }

            this.scanUploadSucceeded = true;
            if (IsOptionalScanPanelActive && this.scanWarnings.Count > 0)
            {
                this.scanNotice = "The scan uploaded with warnings. You can review them below or continue to identify this disc.";
                this.showContinueToIdentify = true;
            }
            else
            {
                NavigateToIdentify();
            }
        }
        catch (OperationCanceledException) when (this.CancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            Console.WriteLine(ex);
            this.uploadError = $"Could not scan the selected disc folder: {ex.Message}";
            AllowIdentifyAfterOptionalScanFailure();
        }
        finally
        {
            this.isScanning = false;
            this.scanProgress = null;
            this.StateHasChanged();
        }
    }

    private void ShowOptionalScanPanel()
    {
        this.startSpinnerTimer?.Dispose();
        this.pollUploadedTimer?.Dispose();
        this.showOptionalScanPanel = true;
        this.showSpinner = false;
        this.uploadError = null;
        this.selectedIndex = ScanTabIndex;
        this.StateHasChanged();
    }

    private void SkipAndIdentify() => NavigateToIdentify();

    private void ContinueToIdentify() => NavigateToIdentify();

    private void NavigateToIdentify()
        => JSRuntime.InvokeVoidAsync("window.location.replace", $"/contribution/{this.ContributionId}/discs/{this.DiscId}/identify");

    private void AllowIdentifyAfterOptionalScanFailure()
    {
        if (IsOptionalScanPanelActive)
        {
            this.showContinueToIdentify = true;
        }
    }

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

    private async Task Upload(UploadChangeEventArgs args, string uri, string contentType, string description)
    {
        try
        {
            var file = args.Files.FirstOrDefault();
            if (file == null)
            {
                return;
            }

            using var stream = file.File.OpenReadStream(long.MaxValue);
            using var reader = new StreamReader(stream);
            string contents = await reader.ReadToEndAsync(this.CancellationToken);
            var content = new StringContent(contents, Encoding.UTF8, contentType);
            var response = await HttpClient.PostAsync(uri, content, this.CancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                var body = await response.Content.ReadAsStringAsync(this.CancellationToken);
                uploadError = ExtractErrorDetail(body, description);
                StateHasChanged();
            }
        }
        catch (Exception ex)
        {
            uploadError = $"An unexpected error occurred uploading the {description}.";
            Console.WriteLine(ex.Message);
            StateHasChanged();
        }
    }

    private async Task ResetError()
    {
        this.uploadError = null;
        this.showSpinner = false;
        this.pollUploadedTimer?.Dispose();
        this.pollUploadedTimer = null;
        this.startSpinnerTimer?.Dispose();

        await HttpClient.DeleteAsync(GetClearErrorUri(), this.CancellationToken);

        this.startSpinnerTimer = new Timer(SpinnerTimerTick!, null, 4000, Timeout.Infinite);
    }

    private static string ExtractErrorDetail(string responseBody, string description)
    {
        string fallback = $"An error occurred uploading the {description}.";
        try
        {
            using var doc = JsonDocument.Parse(responseBody);
            if (doc.RootElement.TryGetProperty("detail", out var detail))
            {
                var text = detail.GetString();
                if (string.IsNullOrEmpty(text))
                {
                    return fallback;
                }

                // The ProblemDetails detail contains a wrapper like:
                // "Unable to save disc logs...\r\nErrors:\r\n- Actual error message"
                // Extract just the error line(s) after "Errors:" for a cleaner message.
                var errorsIndex = text.IndexOf("Errors:", StringComparison.OrdinalIgnoreCase);
                if (errorsIndex >= 0)
                {
                    var errorLines = text[(errorsIndex + "Errors:".Length)..]
                        .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                        .Select(l => l.TrimStart('-', ' '))
                        .Where(l => !string.IsNullOrEmpty(l));
                    var joined = string.Join(" ", errorLines);
                    return string.IsNullOrEmpty(joined) ? fallback : joined;
                }

                return text;
            }
        }
        catch
        {
            // Not valid JSON, fall through
        }

        return fallback;
    }
}
