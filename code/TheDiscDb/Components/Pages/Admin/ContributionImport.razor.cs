using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using TheDiscDb.Client;
using TheDiscDb.Services;
using TheDiscDb.Services.Admin;
using TheDiscDb.Services.Server;
using TheDiscDb.Web.Data;

namespace TheDiscDb.Components.Pages.Admin;

[Authorize(Roles = DefaultRoles.Administrator)]
public partial class ContributionImport : ComponentBase
{
    [Inject]
    private IDbContextFactory<SqlServerDataContext> DbFactory { get; set; } = null!;

    [Inject]
    private UserManager<TheDiscDbUser> UserManager { get; set; } = null!;

    [Inject]
    private IdEncoder IdEncoder { get; set; } = null!;

    [Inject]
    private IClipboardService Clipboard { get; set; } = null!;

    [Inject]
    private IContributionImportOrchestrator Orchestrator { get; set; } = null!;

    [Parameter]
    public string? ContributionId { get; set; }

    private UserContribution? Contribution { get; set; }
    private TheDiscDbUser? User { get; set; }

    private bool IsRunning { get; set; }
    private bool OverwriteFiles { get; set; }
    private bool RunImportStep { get; set; } = true;
    private bool CreatePullRequest { get; set; }
    private bool HasError { get; set; }
    private string PrUrl { get; set; } = string.Empty;
    private List<string> LogLines { get; set; } = new();

    private string WorkflowCommand => $".\\ContributionBuddy.exe generate {this.Contribution?.EncodedId} import pr";
    private string ContributionEncodedId => this.Contribution?.EncodedId ?? string.Empty;
    private string UserDisplayName => this.User?.UserName ?? "Unknown";

    protected override async Task OnInitializedAsync()
    {
        await using var database = await DbFactory.CreateDbContextAsync();

        this.Contribution = await database.UserContributions
            .Include(c => c.Discs)
            .ThenInclude(d => d.Items)
            .FirstOrDefaultAsync(uc => uc.Id.ToString() == this.ContributionId);

        this.IdEncoder.EncodeInPlace(this.Contribution);

        if (!string.IsNullOrEmpty(this.Contribution?.UserId))
        {
            this.User = await UserManager.FindByIdAsync(this.Contribution.UserId);
        }
    }

    private async Task RunImportAsync()
    {
        if (this.Contribution == null || this.IsRunning)
        {
            return;
        }

        this.IsRunning = true;
        this.HasError = false;
        this.PrUrl = string.Empty;
        this.LogLines.Clear();
        StateHasChanged();

        try
        {
            string prUrl = await this.Orchestrator.RunAsync(
                contributionId: this.Contribution.Id,
                overwrite: this.OverwriteFiles,
                import: this.RunImportStep,
                createPr: this.CreatePullRequest,
                log: msg =>
                {
                    this.LogLines.Add(msg);
                    InvokeAsync(StateHasChanged);
                });

            if (!string.IsNullOrEmpty(prUrl))
            {
                this.PrUrl = prUrl;
            }
        }
        catch (Exception ex)
        {
            this.HasError = true;
            this.LogLines.Add($"ERROR: {ex.Message}");
        }
        finally
        {
            this.IsRunning = false;
            await InvokeAsync(StateHasChanged);
        }
    }

    private async Task CopyWorkflowCommandAsync()
    {
        if (!string.IsNullOrEmpty(this.WorkflowCommand))
        {
            await this.Clipboard.WriteTextAsync(this.WorkflowCommand);
        }
    }

    private (string Text, string Url) GetRootAdminLink()
    {
        return ("Admin", "/admin");
    }

    private (string Text, string Url) GetContributionLink()
    {
        return (this.Contribution?.Title ?? "Contribution", $"/admin/contribution/{this.ContributionId}");
    }
}
