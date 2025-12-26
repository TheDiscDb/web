using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using TheDiscDb.Services.Server;
using TheDiscDb.Web.Data;

namespace TheDiscDb.Components.Pages.Admin;

public class EditContributionRequest
{
    [Required]
    public DateTimeOffset ReleaseDate { get; set; }
    [Required]
    [RegularExpression(@"\w{10}", ErrorMessage = "ASIN must be a combination 10 characters or numbers")]
    public string Asin { get; set; } = string.Empty;
    [Required]
    [RegularExpression(@"\d{12,13}", ErrorMessage = "UPC must be exactly 12 digits")]
    public string Upc { get; set; } = string.Empty;
    public string ReleaseTitle { get; set; } = string.Empty;
    [Required]
    public string ReleaseSlug { get; set; } = string.Empty;
    [Required]
    public string RegionCode { get; set; } = string.Empty;
    [Required]
    public string Locale { get; set; } = string.Empty;
    public UserContributionStatus Status { get; set; } = UserContributionStatus.Pending;
}

[Authorize(Roles = DefaultRoles.Administrator)]
public partial class ContributionEdit : ComponentBase, IAsyncDisposable
{
    [Inject]
    private IDbContextFactory<SqlServerDataContext> DbFactory { get; set; } = null!;

    [Inject]
    private UserManager<TheDiscDbUser> UserManager { get; set; } = null!;

    [Inject]
    private IdEncoder IdEncoder { get; set; } = null!;

    [Parameter]
    public string? ContributionId { get; set; }

    private SqlServerDataContext database = default!;
    private UserContribution? Contribution { get; set; }
    private IQueryable<UserContributionDisc>? Discs => Contribution?.Discs.AsQueryable();
    private TheDiscDbUser? User { get; set; }

    private readonly EditContributionRequest request = new EditContributionRequest
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

            if (this.Contribution != null)
            {
                request.Asin = this.Contribution.Asin ?? string.Empty;
                request.Locale = this.Contribution.Locale ?? string.Empty;
                request.RegionCode = this.Contribution.RegionCode ?? string.Empty;
                request.ReleaseDate = this.Contribution.ReleaseDate;
                request.ReleaseSlug = this.Contribution.ReleaseSlug ?? string.Empty;
                request.ReleaseTitle = this.Contribution.ReleaseTitle ?? string.Empty;
                request.Upc = this.Contribution.Upc ?? string.Empty;
                request.Status = this.Contribution.Status;

                this.IdEncoder.EncodeInPlace(this.Contribution);

                if (!string.IsNullOrEmpty(this.Contribution?.UserId))
                {
                    this.User = await UserManager.FindByIdAsync(this.Contribution.UserId);
                }
            }
        }
    }

    public async ValueTask DisposeAsync() => await database.DisposeAsync();

    private async Task HandleValidSubmit(Microsoft.AspNetCore.Components.Forms.EditContext args)
    {
        if (this.Contribution != null)
        {
            this.Contribution.Asin = request.Asin;
            this.Contribution.Locale = request.Locale;
            this.Contribution.RegionCode = request.RegionCode;
            this.Contribution.ReleaseDate = request.ReleaseDate;
            this.Contribution.ReleaseSlug = request.ReleaseSlug;
            this.Contribution.ReleaseTitle = request.ReleaseTitle;
            this.Contribution.Upc = request.Upc;
            this.Contribution.Status = request.Status;
            //database.UserContributions.Update(this.Contribution);
            await database.SaveChangesAsync();

        }
    }
}
