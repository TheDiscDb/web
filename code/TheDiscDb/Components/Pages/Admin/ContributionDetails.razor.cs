using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using TheDiscDb.Client;
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

    [Parameter]
    public string? ContributionId { get; set; }

    private SqlServerDataContext database = default!;
    private UserContribution? Contribution { get; set; }
    private IQueryable<UserContributionDisc>? Discs => Contribution?.Discs.AsQueryable();
    private TheDiscDbUser? User { get; set; }

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

    string PowershellCommand => $".\\ContributionBuddy.exe generate {this.Contribution?.EncodedId}";

    public async ValueTask DisposeAsync() => await database.DisposeAsync();

    private async Task ApproveClicked(Microsoft.AspNetCore.Components.Web.MouseEventArgs args)
    {
        if (this.Contribution != null)
        {
            this.Contribution.Status = UserContributionStatus.Approved;
            await this.database.SaveChangesAsync();
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
            this.Contribution.Status = UserContributionStatus.Imported;
            await this.database.SaveChangesAsync();
        }
    }
}
