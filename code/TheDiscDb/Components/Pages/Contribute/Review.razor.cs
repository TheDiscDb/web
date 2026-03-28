using FluentResults;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Syncfusion.Blazor.Buttons;
using TheDiscDb.Services;
using TheDiscDb.Services.Server;
using TheDiscDb.Validation.Contribution;
using TheDiscDb.Web.Data;

namespace TheDiscDb.Components.Pages.Contribute;

[Authorize]
public partial class Review : ComponentBase, IAsyncDisposable
{
    [Parameter]
    public string? ContributionId { get; set; }

    [Inject]
    public IEnumerable<IContributionValidation>? Validators { get; set; }

    [Inject]
    private NavigationManager NavigationManager { get; set; } = null!;

    [Inject]
    private IDbContextFactory<SqlServerDataContext> DbFactory { get; set; } = null!;

    [Inject]
    private IdEncoder IdEncoder { get; set; } = null!;

    [Inject]
    private IContributionHistoryService HistoryService { get; set; } = null!;

    [Inject]
    private IContributionNotificationService NotificationService { get; set; } = null!;

    [Inject]
    private UserManager<TheDiscDbUser> UserManager { get; set; } = null!;

    [Inject]
    private ILogger<Review> Logger { get; set; } = null!;

    UserContribution? Contribution { get; set; }
    Dictionary<IContributionValidation, Result> Results { get; set; } = new Dictionary<IContributionValidation, Result>();
    private SqlServerDataContext database = default!;

    private bool PassedValidation => this.Results.Values.All(r => r.IsSuccess);
    private bool IsSubmitForReviewButtonDisabled => !PassedValidation;

    protected override async Task OnInitializedAsync()
    {
        if (DbFactory != null && IdEncoder != null)
        {
            var decodedId = this.IdEncoder.Decode(ContributionId!);
            this.database = await DbFactory.CreateDbContextAsync();
            this.Contribution = await database.UserContributions
                .Include(uc => uc.Discs)
                .FirstOrDefaultAsync(uc => uc.Id == decodedId);

            if (this.Contribution != null)
            {
                foreach (var validator in Validators!)
                {
                    var validationResult = await validator.Validate(this.Contribution, CancellationToken.None);
                    this.Results[validator] = validationResult;
                }
            }
        }
    }

    string GetResultCssClass(IContributionValidation validator)
    {
        if (this.Results.TryGetValue(validator, out var result))
        {
            if (result.IsSuccess)
            {
                return "validation-success";
            }
            else
            {
                return "validation-failure";
            }
        }

        return "";
    }

    IconName GetIcon(IContributionValidation validator)
    {
        if (this.Results.TryGetValue(validator, out var result))
        {
            if (result.IsSuccess)
            {
                return IconName.CircleCheck;
            }
            else
            {
                return IconName.CircleClose;
            }
        }
        return IconName.Circle;
    }

    string GetFailureSummary(IContributionValidation validator)
    {
        if (this.Results.TryGetValue(validator, out var result))
        {
            if (result.IsFailed)
            {
                var error = result.Errors.FirstOrDefault();
                if (error != null)
                {
                    return error.Message;
                }
            }
        }

        return "";
    }

    private async Task SubmitContribution(Microsoft.AspNetCore.Components.Web.MouseEventArgs args)
    {
        if (this.Contribution != null)
        {
            var oldStatus = this.Contribution.Status;
            if (oldStatus == UserContributionStatus.ReadyForReview)
            {
                return;
            }

            this.Contribution.Status = UserContributionStatus.ReadyForReview;
            await database.SaveChangesAsync();
            await HistoryService.RecordStatusChangedAsync(this.Contribution.Id, this.Contribution.UserId, oldStatus, UserContributionStatus.ReadyForReview);

            var dbUser = await UserManager.FindByIdAsync(this.Contribution.UserId);
            try
            {
                await NotificationService.NotifyContributionCreatedAsync(this.Contribution, dbUser?.Email, dbUser?.UserName);
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, "Failed to send submission notification for contribution {Id}", this.Contribution.Id);
            }

            this.NavigationManager.NavigateTo("/contribute/my");
        }
    }

    public async ValueTask DisposeAsync() => await database.DisposeAsync();
}