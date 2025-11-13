using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;
using TheDiscDb.Services;

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
    private IUserContributionService Client { get; set; } = null!;

    private readonly string powershellCommandTemplate = "Invoke-WebRequest -Uri \"{0}\" -Method POST -ContentType \"text/plain\" -Body ((& '{1}' --minlength=0 --robot info disc:{2}) | Out-String)";
    private readonly string bashCommandTemplate = "makemkvcon mkv --minlength=0 --robot info disc:{1} 2>&1 | curl -X POST -H \"Content-Type: text/plain\" -d @- {0}";
    //private readonly string powershellLocalCommandTempalte = "Invoke-WebRequest -Uri \"{0}\" -Method POST -ContentType \"text/plain\" -Body ((Get-Content -Path '{1}') | Out-String)";

    State state = new("Copy", "e-icons e-copy-icon");

    public string? PowershellCommand { get; set; }
    public string? BashCommand { get; set; }

    public int DriveIndex { get; set; } = 0;

    private string GetUri() => $"{NavigationManager.BaseUri}api/contribute/{ContributionId}/discs/{DiscId}/logs";

    private string GetMakeMkvPath() => "C:\\Program Files (x86)\\MakeMKV\\makemkvcon64.exe";

    private Timer? timer;

    protected override Task OnInitializedAsync()
    {
        this.PowershellCommand = string.Format(powershellCommandTemplate, GetUri(), GetMakeMkvPath(), this.DriveIndex);
        this.BashCommand = string.Format(bashCommandTemplate, GetUri(), this.DriveIndex);
        this.timer = new Timer(TimerTick!, null, 0, 2000);
        return Task.CompletedTask;
    }

    private void TimerTick(object state)
    {
        this.Client.CheckDiskUploadStatus(this.DiscId ?? string.Empty).ContinueWith(t =>
        {
            if (t != null && t.Result.Value.LogsUploaded)
            {
                this.timer?.Dispose();
                JSRuntime.InvokeVoidAsync("window.location.replace", $"/contribution/{this.ContributionId}/discs/{this.DiscId}/identify");
            }
        });
    }

    private void OnDriveIndexChanged(ChangeEventArgs e)
    {
        this.PowershellCommand = string.Format(powershellCommandTemplate, GetUri(), GetMakeMkvPath(), this.DriveIndex);
    }

    private async Task CopyTextToClipboard()
    {
        if (!string.IsNullOrEmpty(this.PowershellCommand))
        {
            var temp = state;
            state = new("Copied", "e-icons e-circle-check", IsDisabled: true);
            await Clipboard.WriteTextAsync(PowershellCommand);
            await Task.Delay(TimeSpan.FromSeconds(2));
            state = temp;
            await Clipboard.WriteTextAsync(PowershellCommand);
        }
    }
}
