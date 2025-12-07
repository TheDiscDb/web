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

    protected override async Task OnInitializedAsync()
    {
        if (DbFactory != null)
        {
            this.database = await DbFactory.CreateDbContextAsync();

            PendingContributions = database.UserContributions
                .Where(uc => uc.Status == UserContributionStatus.Pending)
                .OrderBy(uc => uc.Created);
        }
    }

    public async ValueTask DisposeAsync() => await database.DisposeAsync();
}
