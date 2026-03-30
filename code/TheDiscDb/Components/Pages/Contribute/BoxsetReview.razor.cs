using FluentResults;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using TheDiscDb.Services;
using TheDiscDb.Services.Server;
using TheDiscDb.Validation.Boxset;
using TheDiscDb.Web.Data;

namespace TheDiscDb.Components.Pages.Contribute;

[Authorize]
public partial class BoxsetReview : ComponentBase
{
    [Parameter]
    public string BoxsetId { get; set; } = string.Empty;

    [Inject]
    IDbContextFactory<SqlServerDataContext> DbFactory { get; set; } = null!;

    [Inject]
    IdEncoder IdEncoder { get; set; } = null!;

    [Inject]
    IEnumerable<IBoxsetValidation> Validators { get; set; } = null!;

    [Inject]
    UserManager<TheDiscDbUser> UserManager { get; set; } = null!;

    [Inject]
    IContributionHistoryService HistoryService { get; set; } = null!;

    [Inject]
    IContributionNotificationService NotificationService { get; set; } = null!;

    [Inject]
    NavigationManager Navigation { get; set; } = null!;

    [CascadingParameter]
    private Task<Microsoft.AspNetCore.Components.Authorization.AuthenticationState>? AuthState { get; set; }

    private UserContributionBoxset? Boxset;
    private Dictionary<string, Result> Results = new();
    private bool isLoading = true;
    private bool isSubmitting;
    private string? errorMessage;

    private bool PassedValidation => Results.Values.All(r => r.IsSuccess);

    private async Task<string?> GetCurrentUserIdAsync()
    {
        if (AuthState == null) return null;
        var state = await AuthState;
        var user = state.User;
        return UserManager.GetUserId(user);
    }

    protected override async Task OnInitializedAsync()
    {
        isLoading = true;

        var userId = await GetCurrentUserIdAsync();
        if (string.IsNullOrEmpty(userId))
        {
            isLoading = false;
            return;
        }

        var decodedId = IdEncoder.Decode(BoxsetId);
        await using var db = await DbFactory.CreateDbContextAsync();

        Boxset = await db.UserContributionBoxsets
            .Include(b => b.Members)
                .ThenInclude(m => m.Disc)
                    .ThenInclude(d => d.UserContribution)
            .FirstOrDefaultAsync(b => b.Id == decodedId && b.UserId == userId);

        if (Boxset != null)
        {
            foreach (var validator in Validators)
            {
                var result = await validator.Validate(Boxset, CancellationToken.None);
                Results[validator.DisplayName] = result;
            }
        }

        isLoading = false;
    }

    private async Task HandleSubmit()
    {
        if (Boxset == null || !PassedValidation) return;

        isSubmitting = true;
        errorMessage = null;

        try
        {
            var userId = await GetCurrentUserIdAsync();
            if (string.IsNullOrEmpty(userId)) return;

            await using var db = await DbFactory.CreateDbContextAsync();

            var boxset = await db.UserContributionBoxsets
                .Include(b => b.Members)
                    .ThenInclude(m => m.Disc)
                        .ThenInclude(d => d.UserContribution)
                .FirstOrDefaultAsync(b => b.Id == Boxset.Id && b.UserId == userId);

            if (boxset == null) return;

            if (boxset.Status != UserContributionStatus.Pending)
            {
                errorMessage = "Only pending boxsets can be submitted for review.";
                return;
            }

            boxset.Status = UserContributionStatus.ReadyForReview;

            // Auto-set all member discs' parent contributions' release slugs to match boxset slug
            foreach (var member in boxset.Members)
            {
                member.Disc.UserContribution.ReleaseSlug = boxset.Slug;
                member.Disc.UserContribution.Status = UserContributionStatus.ReadyForReview;
            }

            await db.SaveChangesAsync();

            Navigation.NavigateTo("/contribute/my");
        }
        catch (Exception)
        {
            errorMessage = "An unexpected error occurred. Please try again.";
        }
        finally
        {
            isSubmitting = false;
        }
    }
}
