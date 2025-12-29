using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;
using StrawberryShake;
using Syncfusion.Blazor.Inputs;
using TheDiscDb.Client.Contributions;

namespace TheDiscDb.Client.Pages.Contribute;

record State(string Text, string IconClassName, bool IsDisabled = false);

[Authorize]
public partial class DiscUpload : ComponentBase
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

    private readonly string powershellCommandTemplate = "Invoke-WebRequest -Uri \"{0}\" -Method POST -ContentType \"text/plain\" -Body ((& '{1}' --minlength=0 --robot info disc:{2}) | Out-String)";
    private readonly string bashCommandTemplate = "makemkvcon --minlength=0 --robot info disc:{1} 2>&1 | curl -X POST -H \"Content-Type: text/plain\" --data-binary @- {0}";
    //private readonly string powershellLocalCommandTempalte = "Invoke-WebRequest -Uri \"{0}\" -Method POST -ContentType \"text/plain\" -Body ((Get-Content -Path '{1}') | Out-String)";

    State state = new("Copy", "e-icons e-copy");
    IDiscUploadPageData_MyContributions_Nodes? contribution;

    public string? PowershellCommand => string.Format(powershellCommandTemplate, GetUri(), GetMakeMkvPath(), this.DriveIndex);
    public string? BashCommand => string.Format(bashCommandTemplate, GetUri(), this.DriveIndex);

    public string DriveIndex { get; set; } = "0";

    int selectedIndex = 0;
    private readonly string[] driveIndices = Enumerable.Range(0, 8).Select(i => i.ToString()).ToArray();

    private string GetUri() => $"{NavigationManager.BaseUri}api/contribute/{ContributionId}/discs/{DiscId}/logs";

    private string GetMakeMkvPath() => "C:\\Program Files (x86)\\MakeMKV\\makemkvcon64.exe";

    private Timer? startSpinnerTimer;
    private Timer? pollUploadedTimer;

    private bool showSpinner;

    protected override async Task OnInitializedAsync()
    {
        //this.pollUploadedTimer = new Timer(PollTimerTick!, null, 0, 2000);
        this.startSpinnerTimer = new Timer(SpinnerTimerTick!, null, 4000, Timeout.Infinite);
        
        var response = await this.ContributionClient.DiscUploadPageData.ExecuteAsync(this.ContributionId ?? string.Empty);
        if (response != null && response.IsSuccessResult())
        {
            this.contribution = response.Data!.MyContributions!.Nodes!.FirstOrDefault();
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

        this.ContributionClient.GetDiscUploadStatus.ExecuteAsync(input).ContinueWith(t =>
        {
            if (t != null && !t.IsFaulted && t.Result!.Data!.DiscUploadStatus!.DiscUploadStatus!.LogsUploaded)
            {
                this.pollUploadedTimer?.Dispose();
                JSRuntime.InvokeVoidAsync("window.location.replace", $"/contribution/{this.ContributionId}/discs/{this.DiscId}/identify");
            }
        });
    }

    private async Task CopyTextToClipboard()
    {
        string currentCommand = selectedIndex switch
        {
            1 => this.BashCommand ?? string.Empty,
            _ => this.PowershellCommand ?? string.Empty,
        };

        if (!string.IsNullOrEmpty(currentCommand))
        {
            var temp = state;
            state = new("Copied", "e-icons e-circle-check", IsDisabled: true);
            await Clipboard.WriteTextAsync(currentCommand);
            await Task.Delay(TimeSpan.FromSeconds(2));
            state = temp;
        }
    }

    private async Task ValueChange(UploadChangeEventArgs args)
    {
        try
        {
            var file = args.Files.FirstOrDefault();
            if (file != null)
            {
                using (var stream = file.File.OpenReadStream(long.MaxValue))
                {
                    using var reader = new StreamReader(stream);
                    string contents = await reader.ReadToEndAsync();
                    await HttpClient.PostAsync(GetUri(), new StringContent(contents));
                }
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine(ex.Message);
        }
    }
}
