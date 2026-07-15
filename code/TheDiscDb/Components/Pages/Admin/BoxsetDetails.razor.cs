using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Components;
using Microsoft.EntityFrameworkCore;
using TheDiscDb.Services;
using TheDiscDb.Services.Server;
using TheDiscDb.Web.Data;

namespace TheDiscDb.Components.Pages.Admin;

[Authorize(Policy = "Admin")]
public partial class BoxsetDetails : ComponentBase
{
    [Parameter]
    public string BoxsetId { get; set; } = string.Empty;

    [Inject]
    IDbContextFactory<SqlServerDataContext> DbFactory { get; set; } = null!;

    [Inject]
    public IdEncoder IdEncoder { get; set; } = null!;

    [Inject]
    IContributionHistoryService HistoryService { get; set; } = null!;

    [Inject]
    IContributionNotificationService NotificationService { get; set; } = null!;

    [Inject]
    TheDiscDb.Services.Achievements.IAchievementService AchievementService { get; set; } = null!;

    [Inject]
    ILogger<BoxsetDetails> Logger { get; set; } = null!;

    [Inject]
    NavigationManager Navigation { get; set; } = null!;

    [Inject]
    IMessageService MessageService { get; set; } = null!;

    [Inject]
    IPrincipalProvider PrincipalProvider { get; set; } = null!;

    private UserContributionBoxset? Boxset;
    private string? statusMessage;

    private bool showMessageDialog;
    private string messageDialogHeader = string.Empty;
    private string adminMessage = string.Empty;
    private bool isRequestingChanges;

    protected override async Task OnInitializedAsync()
    {
        await LoadBoxset();
    }

    private async Task LoadBoxset()
    {
        var decodedId = IdEncoder.Decode(BoxsetId);
        await using var db = await DbFactory.CreateDbContextAsync();

        Boxset = await db.UserContributionBoxsets
            .Include(b => b.Members)
                .ThenInclude(m => m.Disc!)
                    .ThenInclude(d => d.UserContribution)
            .FirstOrDefaultAsync(b => b.Id == decodedId);
    }

    private async Task ApproveClicked()
    {
        if (Boxset == null) return;

        await using var db = await DbFactory.CreateDbContextAsync();
        var boxset = await db.UserContributionBoxsets
            .Include(b => b.Members)
                .ThenInclude(m => m.Disc!)
                    .ThenInclude(d => d.UserContribution)
            .FirstOrDefaultAsync(b => b.Id == Boxset.Id);

        if (boxset == null) return;

        boxset.Status = UserContributionStatus.Approved;
        foreach (var member in boxset.Members)
        {
            if (member.Disc?.UserContribution != null)
            {
                member.Disc.UserContribution.Status = UserContributionStatus.Approved;
            }
        }

        await db.SaveChangesAsync();
        statusMessage = "Boxset and all member contributions marked as Approved.";
        Boxset = boxset;
    }

    private async Task MarkAsImported()
    {
        if (Boxset == null) return;

        await using var db = await DbFactory.CreateDbContextAsync();
        var boxset = await db.UserContributionBoxsets
            .Include(b => b.Members)
                .ThenInclude(m => m.Disc!)
                    .ThenInclude(d => d.UserContribution)
            .FirstOrDefaultAsync(b => b.Id == Boxset.Id);

        if (boxset == null) return;

        boxset.Status = UserContributionStatus.Imported;
        foreach (var member in boxset.Members)
        {
            if (member.Disc?.UserContribution != null)
            {
                member.Disc.UserContribution.Status = UserContributionStatus.Imported;
            }
        }

        await db.SaveChangesAsync();
        statusMessage = "Boxset and all member contributions marked as Imported.";
        Boxset = boxset;

        // Award achievements for every distinct contributor affected by this import.
        var affectedUserIds = boxset.Members
            .Select(m => m.Disc?.UserContribution?.UserId)
            .Append(boxset.UserId)
            .Where(id => !string.IsNullOrEmpty(id))
            .Select(id => id!)
            .Distinct();

        foreach (var affectedUserId in affectedUserIds)
        {
            try
            {
                await AchievementService.EvaluateUserAsync(affectedUserId, AchievementAuditActor.System);
            }
            catch (Exception ex)
            {
                Logger.LogWarning(ex, "Achievement evaluation failed after importing boxset for user {UserId}", affectedUserId);
            }
        }
    }

    private void ShowMessageDialog(bool requestChanges)
    {
        isRequestingChanges = requestChanges;
        messageDialogHeader = requestChanges ? "Request Changes" : "Reject Boxset";
        adminMessage = string.Empty;
        showMessageDialog = true;
    }

    private void CancelMessageDialog()
    {
        showMessageDialog = false;
        adminMessage = string.Empty;
    }

    private async Task ConfirmStatusChange()
    {
        if (Boxset == null) return;

        var newStatus = isRequestingChanges
            ? UserContributionStatus.ChangesRequested
            : UserContributionStatus.Rejected;

        await using var db = await DbFactory.CreateDbContextAsync();
        var boxset = await db.UserContributionBoxsets
            .Include(b => b.Members)
                .ThenInclude(m => m.Disc!)
                    .ThenInclude(d => d.UserContribution)
            .FirstOrDefaultAsync(b => b.Id == Boxset.Id);

        if (boxset == null) return;

        boxset.Status = newStatus;
        foreach (var member in boxset.Members)
        {
            if (member.Disc?.UserContribution != null)
            {
                member.Disc.UserContribution.Status = newStatus;
            }
        }

        await db.SaveChangesAsync();

        // Persist the admin's message so the user can see it on the boxset's messages page.
        // Without this the dialog's text was silently dropped.
        if (!string.IsNullOrWhiteSpace(adminMessage))
        {
            var adminUserId = PrincipalProvider.Principal?.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
            if (!string.IsNullOrEmpty(adminUserId))
            {
                await MessageService.SendAdminBoxsetMessageAsync(boxset.Id, adminUserId, boxset.UserId, adminMessage);
            }
        }

        showMessageDialog = false;
        statusMessage = $"Boxset and all member contributions set to {newStatus}.";
        Boxset = boxset;
    }

    private static (string Text, string Url) GetRootAdminLink() => ("Admin", "/admin");
}
