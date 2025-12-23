using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using TheDiscDb.Services;
using TheDiscDb.Web.Data;

namespace TheDiscDb.Components.Pages.Admin;

[Authorize(Roles = DefaultRoles.Administrator)]
public partial class DiscDetails : ComponentBase, IAsyncDisposable
{
    [Inject]
    private IDbContextFactory<SqlServerDataContext> DbFactory { get; set; } = null!;

    [Inject]
    private UserManager<TheDiscDbUser> UserManager { get; set; } = null!;

    [Parameter]
    public string? ContributionId { get; set; }

    [Parameter]
    public string? DiscId { get; set; }

    private SqlServerDataContext database = default!;
    private UserContribution? Contribution { get; set; }
    private UserContributionDisc? Disc { get; set; }
    private TheDiscDbUser? User { get; set; }

    readonly string[] formats = ["4K", "Blu-ray", "DVD"]; // TODO: Centralize this somewhere

    private readonly SaveDiscRequest request = new SaveDiscRequest
    {
    };

    protected override async Task OnInitializedAsync()
    {
        if (DbFactory != null)
        {
            this.database = await DbFactory.CreateDbContextAsync();

            this.Contribution = await database.UserContributions
                .Include(c => c.Discs)
                .ThenInclude(d => d.Items)
                .FirstOrDefaultAsync(uc => uc.Id.ToString() == ContributionId);

            if (this.Contribution != null && !string.IsNullOrEmpty(this.DiscId))
            {
                this.Disc = this.Contribution.Discs.FirstOrDefault(d => d.Id.ToString() == this.DiscId);
                if (this.Disc != null)
                {
                    request.ContentHash = this.Disc.ContentHash;
                    request.Format = this.Disc.Format;
                    request.Name = this.Disc.Name;
                    request.Slug = this.Disc.Slug;
                }
            }

            if (!string.IsNullOrEmpty(this.Contribution?.UserId))
            {
                this.User = await UserManager.FindByIdAsync(this.Contribution.UserId);
            }
        }
    }

    Task HandleValidSubmit()
    {
        return Task.CompletedTask;
    }


    public async ValueTask DisposeAsync() => await database.DisposeAsync();
}
