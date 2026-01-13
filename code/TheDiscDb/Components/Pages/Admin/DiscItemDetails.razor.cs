using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using TheDiscDb.Web.Data;

namespace TheDiscDb.Components.Pages.Admin;

[Authorize(Roles = DefaultRoles.Administrator)]
public partial class DiscItemDetails : ComponentBase, IAsyncDisposable
{
    [Inject]
    private IDbContextFactory<SqlServerDataContext> DbFactory { get; set; } = null!;

    [Inject]
    private UserManager<TheDiscDbUser> UserManager { get; set; } = null!;

    [Parameter]
    public string? ContributionId { get; set; }

    [Parameter]
    public string? DiscId { get; set; }

    [Parameter]
    public string? ItemId { get; set; }

    private SqlServerDataContext database = default!;
    private UserContribution? Contribution { get; set; }
    private UserContributionDisc? Disc { get; set; }
    private UserContributionDiscItem? Item { get; set; }
    private TheDiscDbUser? User { get; set; }

    readonly string[] itemTypes = ["MainMovie", "Extra", "Episode", "DeletedScene", "Trailer"];

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
                if (Disc != null)
                {
                    this.Item = Disc.Items.FirstOrDefault(i => i.Id.ToString() == this.ItemId);
                }
            }

            if (!string.IsNullOrEmpty(this.Contribution?.UserId))
            {
                this.User = await UserManager.FindByIdAsync(this.Contribution.UserId);
            }
        }
    }

    async Task HandleValidSubmit()
    {
        if (this.Item != null)
        {
            await this.database.SaveChangesAsync();
        }
    }


    public async ValueTask DisposeAsync() => await database.DisposeAsync();
}
