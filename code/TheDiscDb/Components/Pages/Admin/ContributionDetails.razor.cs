using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using TheDiscDb.Client;
using TheDiscDb.Services;
using TheDiscDb.Services.Server;
using TheDiscDb.Web.Data;

namespace TheDiscDb.Components.Pages.Admin;

[Authorize(Roles = DefaultRoles.Administrator)]
public partial class ContributionDetails : ComponentBase, IAsyncDisposable
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
    private IContributionHistoryService HistoryService { get; set; } = null!;

    [Inject]
    private IPrincipalProvider PrincipalProvider { get; set; } = null!;

    [Parameter]
    public string? ContributionId { get; set; }

    private SqlServerDataContext database = default!;
    private UserContribution? Contribution { get; set; }
    private IQueryable<UserContributionDisc>? Discs => Contribution?.Discs.AsQueryable();
    private TheDiscDbUser? User { get; set; }

    protected override async Task OnInitializedAsync()
    {
        if (DbFactory != null)
        {
            this.database = await DbFactory.CreateDbContextAsync();

            this.Contribution = await database.UserContributions
                .Include(c => c.Discs)
                .ThenInclude(d => d.Items)
                .FirstOrDefaultAsync(uc => uc.Id.ToString() == ContributionId);

            this.IdEncoder.EncodeInPlace(this.Contribution);

            if (!string.IsNullOrEmpty(this.Contribution?.UserId))
            {
                this.User = await UserManager.FindByIdAsync(this.Contribution.UserId);
            }
        }
    }

    string GetTmdbLink()
    {
        if (string.IsNullOrEmpty(this.Contribution?.ExternalId))
        {
            return "";
        }

        string type = "movie";
        if (this.Contribution.MediaType.Equals("series", StringComparison.OrdinalIgnoreCase))
        {
            type = "tv";
        }

        return $"https://www.themoviedb.org/{type}/{this.Contribution?.ExternalId}";
    }

    string PowershellCommand => $".\\ContributionBuddy.exe generate {this.Contribution?.EncodedId}";

    public async ValueTask DisposeAsync() => await database.DisposeAsync();

    private async Task ApproveClicked(Microsoft.AspNetCore.Components.Web.MouseEventArgs args)
    {
        if (this.Contribution != null)
        {
            var oldStatus = this.Contribution.Status;
            this.Contribution.Status = UserContributionStatus.Approved;
            await this.database.SaveChangesAsync();
            await HistoryService.RecordStatusChangedAsync(this.Contribution.Id, this.Contribution.UserId, oldStatus, UserContributionStatus.Approved);
        }
    }

    private async Task CopyTextToClipboard()
    {
        if (!string.IsNullOrEmpty(PowershellCommand))
        {
            await Clipboard.WriteTextAsync(PowershellCommand);
        }
    }
    private async Task MarkAsImported(Microsoft.AspNetCore.Components.Web.MouseEventArgs args)
    {
        if (this.Contribution != null)
        {
            var oldStatus = this.Contribution.Status;
            this.Contribution.Status = UserContributionStatus.Imported;
            await this.database.SaveChangesAsync();
            await HistoryService.RecordStatusChangedAsync(this.Contribution.Id, this.Contribution.UserId, oldStatus, UserContributionStatus.Imported);
        }
    }

    // Status change dialog state
    private bool showStatusMessageDialog;
    private string statusMessage = string.Empty;
    private string statusDialogHeader = string.Empty;
    private string statusDialogAction = string.Empty;
    private UserContributionStatus pendingStatus;

    private void ShowRejectDialog()
    {
        pendingStatus = UserContributionStatus.Rejected;
        statusDialogHeader = "Reject Contribution";
        statusDialogAction = "Reject";
        statusMessage = string.Empty;
        showStatusMessageDialog = true;
    }

    private void ShowChangesRequestedDialog()
    {
        pendingStatus = UserContributionStatus.ChangesRequested;
        statusDialogHeader = "Request Changes";
        statusDialogAction = "Request Changes";
        statusMessage = string.Empty;
        showStatusMessageDialog = true;
    }

    private void CancelStatusDialog()
    {
        showStatusMessageDialog = false;
        statusMessage = string.Empty;
    }

    private async Task ConfirmStatusChange()
    {
        if (this.Contribution == null || string.IsNullOrWhiteSpace(statusMessage))
            return;

        var oldStatus = this.Contribution.Status;
        this.Contribution.Status = pendingStatus;
        await this.database.SaveChangesAsync();
        await HistoryService.RecordStatusChangedAsync(this.Contribution.Id, this.Contribution.UserId, oldStatus, pendingStatus);

        var adminUserId = PrincipalProvider.Principal?.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
        if (!string.IsNullOrEmpty(adminUserId))
        {
            var userMessage = new UserMessage
            {
                ContributionId = this.Contribution.Id,
                FromUserId = adminUserId,
                ToUserId = this.Contribution.UserId,
                Message = statusMessage,
                IsRead = false,
                CreatedAt = DateTimeOffset.UtcNow,
                Type = UserMessageType.AdminMessage
            };
            database.UserMessages.Add(userMessage);
            await database.SaveChangesAsync();
        }

        showStatusMessageDialog = false;
        statusMessage = string.Empty;
    }
}
