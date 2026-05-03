using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Components;
using TheDiscDb.Client.Contributions;
using TheDiscDb.Client.Controls;

namespace TheDiscDb.Client.Pages.Contribute;

[Authorize]
public partial class CreateBoxset : ComponentBase
{
    [Inject]
    IContributionClient ContributionClient { get; set; } = null!;

    [Inject]
    NavigationManager Navigation { get; set; } = null!;

    private BoxsetEditableRequest request = new()
    {
        Locale = "en-us",
        RegionCode = "1",
    };

    private ReleaseDateInput? releaseDateInput;
    private bool isSubmitting;
    private string? errorMessage;

    private void OnTitleChanged(ChangeEventArgs e)
    {
        var title = e.Value?.ToString() ?? string.Empty;
        request.Title = title;
        request.Slug = title.Slugify();
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

            var result = await ContributionClient.CreateBoxset.ExecuteAsync(new CreateBoxsetInput
            {
                Input = input
            });

            if (result.Data?.CreateBoxset?.Errors is { Count: > 0 } errors)
            {
                errorMessage = errors[0] switch
                {
                    ICreateBoxset_CreateBoxset_Errors_AuthenticationError e => e.Message,
                    _ => "An unexpected error occurred."
                };
                return;
            }

            var encodedId = result.Data?.CreateBoxset?.UserContributionBoxset?.EncodedId;
            if (!string.IsNullOrEmpty(encodedId))
            {
                Navigation.NavigateTo($"/contribution/boxset/{encodedId}");
            }
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
