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
    NavigationManager Navigation { get; set; } = null!;

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
                .ThenInclude(m => m.Disc)
                    .ThenInclude(d => d.UserContribution)
            .FirstOrDefaultAsync(b => b.Id == decodedId);
    }

    private async Task ApproveClicked()
    {
        if (Boxset == null) return;

        await using var db = await DbFactory.CreateDbContextAsync();
        var boxset = await db.UserContributionBoxsets
            .Include(b => b.Members)
                .ThenInclude(m => m.Disc)
                    .ThenInclude(d => d.UserContribution)
            .FirstOrDefaultAsync(b => b.Id == Boxset.Id);

        if (boxset == null) return;

        boxset.Status = UserContributionStatus.Approved;
        foreach (var member in boxset.Members)
        {
            member.Disc.UserContribution.Status = UserContributionStatus.Approved;
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
                .ThenInclude(m => m.Disc)
                    .ThenInclude(d => d.UserContribution)
            .FirstOrDefaultAsync(b => b.Id == Boxset.Id);

        if (boxset == null) return;

        boxset.Status = UserContributionStatus.Imported;
        foreach (var member in boxset.Members)
        {
            member.Disc.UserContribution.Status = UserContributionStatus.Imported;
        }

        await db.SaveChangesAsync();
        statusMessage = "Boxset and all member contributions marked as Imported.";
        Boxset = boxset;
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
                .ThenInclude(m => m.Disc)
                    .ThenInclude(d => d.UserContribution)
            .FirstOrDefaultAsync(b => b.Id == Boxset.Id);

        if (boxset == null) return;

        boxset.Status = newStatus;
        foreach (var member in boxset.Members)
        {
            member.Disc.UserContribution.Status = newStatus;
        }

        await db.SaveChangesAsync();

        showMessageDialog = false;
        statusMessage = $"Boxset and all member contributions set to {newStatus}.";
        Boxset = boxset;
    }

    private static (string Text, string Url) GetRootAdminLink() => ("Admin", "/admin");
}
