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
    private IContributionNotificationService NotificationService { get; set; } = null!;

    [Inject]
    private IMessageService MessageService { get; set; } = null!;

    [Inject]
    private IPrincipalProvider PrincipalProvider { get; set; } = null!;

    [Inject]
    private ILogger<ContributionDetails> logger { get; set; } = null!;

    [Parameter]
    public string? ContributionId { get; set; }

    private SqlServerDataContext database = default!;
    private UserContribution? Contribution { get; set; }
    private List<UserContributionDisc>? discList;
    private TheDiscDbUser? User { get; set; }

    private UserContributionDisc? draggedDisc;
    private UserContributionDisc? dragOverDisc;
    private bool isSaving;

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

            if (this.Contribution != null)
            {
                this.discList = this.Contribution.Discs
                    .OrderBy(d => d.Index ?? int.MaxValue)
                    .ThenBy(d => d.Id)
                    .ToList();
            }

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

    string PowershellCommand => $".\\ContributionBuddy.exe generate {this.Contribution?.EncodedId} import pr";

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

            try
            {
                await NotificationService.NotifyContributionImportedAsync(this.Contribution, this.User?.Email);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Failed to send imported notification for contribution {Id}", this.Contribution.Id);
            }
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
            await MessageService.SendAdminMessageAsync(this.Contribution.Id, adminUserId, this.Contribution.UserId, statusMessage);
        }

        showStatusMessageDialog = false;
        statusMessage = string.Empty;
    }

    private void OnDragStart(UserContributionDisc disc)
    {
        if (isSaving) return;
        draggedDisc = disc;
    }

    private void OnDragEnter(UserContributionDisc disc)
    {
        if (draggedDisc == null || disc == draggedDisc || discList == null || isSaving) return;
        dragOverDisc = disc;

        int fromIndex = discList.IndexOf(draggedDisc);
        int toIndex = discList.IndexOf(disc);
        if (fromIndex < 0 || toIndex < 0) return;

        discList.RemoveAt(fromIndex);
        discList.Insert(toIndex, draggedDisc);
    }

    private async Task OnDragEnd()
    {
        var wasDragging = draggedDisc != null;
        draggedDisc = null;
        dragOverDisc = null;

        if (!wasDragging || isSaving || discList == null) return;

        isSaving = true;
        try
        {
            RebuildIndices(discList);
            await this.database.SaveChangesAsync();
        }
        finally
        {
            isSaving = false;
        }
    }

    private string GetRowClass(UserContributionDisc disc)
    {
        if (disc == draggedDisc) return "dragging";
        if (disc == dragOverDisc) return "drag-over";
        return string.Empty;
    }

    private static void RebuildIndices(List<UserContributionDisc> discs)
    {
        for (int i = 0; i < discs.Count; i++)
        {
            discs[i].Index = i + 1;
        }
    }
}
