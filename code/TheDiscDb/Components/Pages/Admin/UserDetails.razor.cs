using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using TheDiscDb.Web.Data;

namespace TheDiscDb.Components.Pages.Admin;

[Authorize(Roles = DefaultRoles.Administrator)]
public partial class UserDetails : ComponentBase, IAsyncDisposable
{
    [Inject]
    private IDbContextFactory<SqlServerDataContext> DbFactory { get; set; } = null!;

    [Inject]
    private UserManager<TheDiscDbUser> UserManager { get; set; } = null!;

    [Parameter]
    public string? UserId { get; set; }

    private SqlServerDataContext database = default!;
    private TheDiscDbUser? User { get; set; }
    private IQueryable<UserContribution>? Contributions { get; set; }

    protected override async Task OnInitializedAsync()
    {
        if (DbFactory != null)
        {
            this.database = await DbFactory.CreateDbContextAsync();

            if (!string.IsNullOrEmpty(UserId))
            {
                this.User = await UserManager.FindByIdAsync(UserId);
                this.Contributions = this.database.UserContributions.Where(c => c.UserId == UserId);
            }
        }
    }

    public async ValueTask DisposeAsync() => await database.DisposeAsync();
}
