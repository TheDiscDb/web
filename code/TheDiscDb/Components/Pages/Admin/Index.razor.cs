using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Components;
using Microsoft.EntityFrameworkCore;
using TheDiscDb.Web.Data;

namespace TheDiscDb.Components.Pages.Admin;

[Authorize(Roles = DefaultRoles.Administrator)]
public partial class Index : ComponentBase, IAsyncDisposable
{
    [Inject]
    private IDbContextFactory<SqlServerDataContext> DbFactory { get; set; } = null!;

    private SqlServerDataContext database = default!;
    private IQueryable<UserContribution>? PendingContributions { get; set; }
    private readonly UserContributionStatus[] statusList = Enum.GetValues<UserContributionStatus>();
    private UserContributionStatus selectedStatus = UserContributionStatus.ReadyForReview;

    protected override async Task OnInitializedAsync()
    {
        await RefreshList();
    }

    private async Task RefreshList()
    {
        if (DbFactory != null)
        {
            this.database = await DbFactory.CreateDbContextAsync();

            PendingContributions = database.UserContributions
                .Where(uc => uc.Status == this.selectedStatus)
                .OrderBy(uc => uc.Created);
        }
    }

    public async ValueTask DisposeAsync() => await database.DisposeAsync();
    
    private async Task OnStatusChanged(UserContributionStatus value)
    {
        selectedStatus = value;
        await RefreshList();
    }
}
