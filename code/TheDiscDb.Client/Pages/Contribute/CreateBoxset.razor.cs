using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Components;
using TheDiscDb.Client.Contributions;

namespace TheDiscDb.Client.Pages.Contribute;

[Authorize]
public partial class CreateBoxset : ComponentBase
{
    [Inject]
    IContributionClient ContributionClient { get; set; } = null!;

    [Inject]
    NavigationManager Navigation { get; set; } = null!;

    private BoxsetMutationRequestInput request = new()
    {
        Title = string.Empty,
        Slug = string.Empty,
    };

    private DateTimeOffset? releaseDate;
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
            if (releaseDate.HasValue)
            {
                request.ReleaseDate = releaseDate.Value;
            }

            var result = await ContributionClient.CreateBoxset.ExecuteAsync(new CreateBoxsetInput
            {
                Input = request
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
