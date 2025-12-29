using FluentResults;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Components;
using Microsoft.EntityFrameworkCore;
using Syncfusion.Blazor.Buttons;
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

    UserContribution? Contribution { get; set; }
    Dictionary<IContributionValidation, Result> Results { get; set; } = new Dictionary<IContributionValidation, Result>();
    private SqlServerDataContext database = default!;

    private bool PassedValidation => this.Results.Values.All(r => r.IsSuccess);
    private bool IsSubmitForReviewButtonDisabled => !PassedValidation;

    protected override async Task OnInitializedAsync()
    {
        if (DbFactory != null)
        {
            this.database = await DbFactory.CreateDbContextAsync();
            this.Contribution = await database.UserContributions
                .Include(uc => uc.Discs)
                .FirstOrDefaultAsync(uc => uc.Id.ToString() == ContributionId);

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
            this.Contribution.Status = UserContributionStatus.ReadyForReview;
            await database.SaveChangesAsync();
            this.NavigationManager.NavigateTo("/contribute/my");
        }
    }

    public async ValueTask DisposeAsync() => await database.DisposeAsync();
}