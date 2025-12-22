using FluentResults;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Components;
using Syncfusion.Blazor.Buttons;
using TheDiscDb.Services;
using TheDiscDb.Validation.Contribution;
using TheDiscDb.Web.Data;

namespace TheDiscDb.Components.Pages.Contribute;

[Authorize]
public partial class Review : ComponentBase
{
    [Parameter]
    public string? ContributionId { get; set; }

    [Inject]
    public IEnumerable<IContributionValidation>? Validators { get; set; }

    [Inject]
    private NavigationManager NavigationManager { get; set; } = null!;

    [Inject]
    private IUserContributionService Client { get; set; } = null!;

    UserContribution? Contribution { get; set; }
    Dictionary<IContributionValidation, Result> Results { get; set; } = new Dictionary<IContributionValidation, Result>();

    private bool PassedValidation => this.Results.Values.All(r => r.IsSuccess);
    private bool IsSubmitForReviewButtonDisabled => !PassedValidation;

    protected override async Task OnInitializedAsync()
    {
        var result = await this.Client.GetContribution(ContributionId!);
        if (result != null && result.IsSuccess)
        {
            this.Contribution = result.Value;

            foreach (var validator in Validators!)
            {
                var validationResult = await validator.Validate(this.Contribution, CancellationToken.None);
                this.Results[validator] = validationResult;
            }
        }
    }

    //protected override async Task OnAfterRenderAsync(bool firstRender)
    //{
    //    if (firstRender && this.Contribution != null)
    //    {
    //        foreach (var validator in Validators!)
    //        {
    //            var result = await validator.Validate(this.Contribution, CancellationToken.None);
    //            this.Results[validator] = result;
    //        }
    //        this.StateHasChanged();
    //    }
    //}

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

            var request = new ContributionMutationRequest(this.Contribution);

            var response = await this.Client.UpdateContribution(this.Contribution.EncodedId, request);
            if (response != null && response.IsSuccess)
            {
                //redirect to /contribute/my
                this.NavigationManager.NavigateTo("/contribute/my");
            }
        }
    }
}