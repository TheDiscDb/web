using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Components;
using Microsoft.EntityFrameworkCore;
using Sqids;
using TheDiscDb.Web.Data;

namespace TheDiscDb.Components.Pages;

[Authorize]
public partial class ContributionDiscs : ComponentBase
{
    [Parameter]
    public string? ContributionId { get; set; }

    [Inject]
    IDbContextFactory<SqlServerDataContext>? Context { get; set; }

    [Inject]
    SqidsEncoder<int>? IdEncoder { get; set; }

    private UserContribution? Contribution { get; set; }

    private IQueryable<UserContributionDisc>? Discs => Contribution?.Discs.OrderBy(d => d.Index).AsQueryable();

    protected override async Task OnInitializedAsync()
    {
        if (this.Context == null)
        {
            throw new Exception("Context was not injected");
        }

        if (this.IdEncoder == null)
        {
            throw new Exception("IdEncoder was not injected");
        }

        var context = await this.Context.CreateDbContextAsync();
        int decodedId = this.IdEncoder.Decode(ContributionId).Single();
        this.Contribution = await context.UserContributions
            .Include(c => c.Discs)
            .ThenInclude(d => d.Items)
            .FirstOrDefaultAsync(c => c.Id.ToString() == ContributionId);
    }
}
