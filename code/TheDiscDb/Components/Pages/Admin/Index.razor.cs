using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Components;
using Microsoft.EntityFrameworkCore;
using TheDiscDb.Services.Server;
using TheDiscDb.Web.Data;

namespace TheDiscDb.Components.Pages.Admin;

[Authorize(Roles = DefaultRoles.Administrator)]
public partial class Index : ComponentBase
{
    [Inject]
    private IDbContextFactory<SqlServerDataContext> DbFactory { get; set; } = null!;

    [Inject]
    private IdEncoder IdEncoder { get; set; } = null!;

    private IQueryable<UserContribution>? PendingContributions { get; set; }
    private IQueryable<UserContributionBoxset>? PendingBoxsets { get; set; }
    private readonly UserContributionStatus[] statusList = Enum.GetValues<UserContributionStatus>();
    private UserContributionStatus selectedStatus = UserContributionStatus.ReadyForReview;

    protected override async Task OnInitializedAsync()
    {
        await RefreshList();
    }

    private async Task RefreshList()
    {
        // Materialize both queries into in-memory lists so the two QuickGrids can sort/render
        // independently without sharing a live DbContext (which throws on concurrent access).
        // Each query uses its own short-lived context from the factory.
        await using var contribDb = await DbFactory.CreateDbContextAsync();
        var contributions = await contribDb.UserContributions
            .AsNoTracking()
            .Include(uc => uc.Discs)
            .Where(uc => uc.Status == this.selectedStatus)
            .OrderBy(uc => uc.Created)
            .ToListAsync();
        PendingContributions = contributions.AsQueryable();

        await using var boxsetDb = await DbFactory.CreateDbContextAsync();
        var boxsets = await boxsetDb.UserContributionBoxsets
            .AsNoTracking()
            .Include(b => b.Members)
            .Where(b => b.Status == this.selectedStatus)
            .OrderBy(b => b.Created)
            .ToListAsync();
        PendingBoxsets = boxsets.AsQueryable();
    }

    private async Task OnStatusChanged(UserContributionStatus value)
    {
        selectedStatus = value;
        await RefreshList();
    }
}
