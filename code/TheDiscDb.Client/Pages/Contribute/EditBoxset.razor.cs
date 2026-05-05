using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Components;
using StrawberryShake;
using TheDiscDb.Client.Contributions;
using TheDiscDb.Client.Controls;

namespace TheDiscDb.Client.Pages.Contribute;

[Authorize]
public partial class EditBoxset : CancellableComponentBase
{
    [Parameter]
    public string BoxsetId { get; set; } = string.Empty;

    [Inject]
    IContributionClient ContributionClient { get; set; } = null!;

    [Inject]
    NavigationManager Navigation { get; set; } = null!;

    private BoxsetEditableRequest request = new();

    private ReleaseDateInput? releaseDateInput;
    private bool isLoading = true;
    private bool isSubmitting;
    private bool boxsetFound;
    private string? errorMessage;

    protected override async Task OnInitializedAsync()
    {
        try
        {
            var filter = new UserContributionBoxsetFilterInput
            {
                EncodedId = new EncodedIdOperationFilterInput { Eq = BoxsetId }
            };

            var result = await ContributionClient.GetBoxsetDetail.ExecuteAsync(filter, this.CancellationToken);
            if (result != null && result.IsSuccessResult())
            {
                var boxset = result.Data?.MyBoxsets?.Nodes?.FirstOrDefault();
                if (boxset != null)
                {
                    if (!boxset.Status.IsEditableByOwner())
                    {
                        errorMessage = $"This boxset cannot be edited (status: {boxset.Status}).";
                    }
                    else
                    {
                        request.Title = boxset.Title;
                        request.SortTitle = boxset.SortTitle;
                        request.Slug = boxset.Slug;
                        request.Asin = boxset.Asin;
                        request.Upc = boxset.Upc;
                        request.Locale = boxset.Locale;
                        request.RegionCode = boxset.RegionCode;
                        request.ReleaseDate = boxset.ReleaseDate;
                        boxsetFound = true;
                    }
                }
                else
                {
                    errorMessage = "Boxset not found.";
                }
            }
            else
            {
                errorMessage = "Failed to load boxset.";
            }
        }
        catch (Exception)
        {
            errorMessage = "Failed to load boxset.";
        }
        finally
        {
            isLoading = false;
        }
    }

    private void OnTitleChanged(ChangeEventArgs e)
    {
        var title = e.Value?.ToString() ?? string.Empty;
        request.Title = title;
        request.Slug = title.Slugify();
        // Clear so the server regenerates SortTitle from the new Title.
        request.SortTitle = null;
    }

    private async Task HandleSubmit()
    {
        isSubmitting = true;
        errorMessage = null;

        try
        {
            if (releaseDateInput != null && !releaseDateInput.Validate())
            {
                return;
            }

            var input = new BoxsetMutationRequestInput
            {
                Title = request.Title,
                SortTitle = request.SortTitle,
                Slug = request.Slug,
                Asin = request.Asin,
                Upc = request.Upc,
                Locale = request.Locale,
                RegionCode = request.RegionCode,
                ReleaseDate = request.ReleaseDate,
            };

            var result = await ContributionClient.UpdateBoxset.ExecuteAsync(new UpdateBoxsetInput
            {
                BoxsetId = BoxsetId,
                Input = input
            });

            if (result.Data?.UpdateBoxset?.Errors is { Count: > 0 } errors)
            {
                errorMessage = errors[0] switch
                {
                    IUpdateBoxset_UpdateBoxset_Errors_AuthenticationError e => e.Message,
                    IUpdateBoxset_UpdateBoxset_Errors_BoxsetNotFoundError e => e.Message,
                    IUpdateBoxset_UpdateBoxset_Errors_InvalidIdError e => e.Message,
                    IUpdateBoxset_UpdateBoxset_Errors_InvalidOwnershipError e => e.Message,
                    _ => "An unexpected error occurred."
                };
                return;
            }

            Navigation.NavigateTo($"/contribution/boxset/{BoxsetId}");
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
