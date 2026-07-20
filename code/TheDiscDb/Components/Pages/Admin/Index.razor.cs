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

    [Inject]
    private NavigationManager Navigation { get; set; } = null!;

    // Persisted in the query string so the chosen status survives navigation away and back.
    [SupplyParameterFromQuery(Name = "status")]
    public string? StatusQuery { get; set; }

    private IQueryable<UserContribution>? PendingContributions { get; set; }
    private IQueryable<UserContributionBoxset>? PendingBoxsets { get; set; }
    private readonly UserContributionStatus[] statusList = Enum.GetValues<UserContributionStatus>();
    private UserContributionStatus selectedStatus = UserContributionStatus.ReadyForReview;
    private UserContributionStatus? loadedStatus;

    protected override async Task OnParametersSetAsync()
    {
        selectedStatus = Enum.TryParse<UserContributionStatus>(this.StatusQuery, ignoreCase: true, out var parsed)
            ? parsed
            : UserContributionStatus.ReadyForReview;

        // Only hit the database when the effective status actually changed (query navigation or
        // first load), not on every re-render.
        if (this.loadedStatus != selectedStatus)
        {
            this.loadedStatus = selectedStatus;
            await RefreshList();
        }
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

    private void OnStatusChanged(UserContributionStatus value)
    {
        // Update the URL; OnParametersSetAsync re-reads the query and refreshes the list.
        this.Navigation.NavigateTo(
            this.Navigation.GetUriWithQueryParameter("status", value.ToString()));
    }
}
