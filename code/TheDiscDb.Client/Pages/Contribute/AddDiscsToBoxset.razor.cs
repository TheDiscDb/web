using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Components;
using StrawberryShake;
using TheDiscDb.Client.Contributions;

namespace TheDiscDb.Client.Pages.Contribute;

[Authorize]
public partial class AddDiscsToBoxset : ComponentBase
{
    [Parameter]
    public string BoxsetId { get; set; } = string.Empty;

    [Inject]
    IContributionClient ContributionClient { get; set; } = null!;

    [Inject]
    NavigationManager Navigation { get; set; } = null!;

    private IReadOnlyList<IGetEligibleContributions_MyContributions_Nodes>? Contributions;
    private HashSet<string> addedDiscIds = new();
    private HashSet<string> addingDiscIds = new();
    private bool isLoading = true;
    private string? errorMessage;
    private string? successMessage;

    protected override async Task OnInitializedAsync()
    {
        await LoadExistingBoxsetMembers();
        await LoadContributions();
    }

    private async Task LoadExistingBoxsetMembers()
    {
        try
        {
            var filter = new UserContributionBoxsetFilterInput
            {
                EncodedId = new EncodedIdOperationFilterInput { Eq = BoxsetId }
            };

            var result = await ContributionClient.GetBoxsetDetail.ExecuteAsync(filter);
            if (result != null && result.IsSuccessResult())
            {
                var boxset = result.Data?.MyBoxsets?.Nodes?.FirstOrDefault();
                if (boxset?.Members != null)
                {
                    foreach (var member in boxset.Members)
                    {
                        addedDiscIds.Add(member.Disc.EncodedId);
                    }
                }
            }
        }
        catch (Exception)
        {
            // Non-fatal — we just won't know which discs are already added
        }
    }

    private async Task LoadContributions()
    {
        isLoading = true;
        try
        {
            var result = await ContributionClient.GetEligibleContributions.ExecuteAsync(100, null);
            if (result != null && result.IsSuccessResult())
            {
                Contributions = result.Data?.MyContributions?.Nodes;
            }
        }
        catch (Exception)
        {
            errorMessage = "Failed to load contributions.";
        }
        finally
        {
            isLoading = false;
        }
    }

    private async Task AddDisc(string discId)
    {
        if (addingDiscIds.Contains(discId)) return;

        addingDiscIds.Add(discId);
        errorMessage = null;
        successMessage = null;

        try
        {
            var result = await ContributionClient.AddDiscToBoxset.ExecuteAsync(new AddDiscToBoxsetInput
            {
                BoxsetId = BoxsetId,
                DiscId = discId
            });

            if (result == null || !result.IsSuccessResult())
            {
                errorMessage = "Failed to add disc — server error. Please try again.";
                return;
            }

            if (result.Data?.AddDiscToBoxset?.Errors is { Count: > 0 } errors)
            {
                var error = errors[0];
                errorMessage = error switch
                {
                    IAddDiscToBoxset_AddDiscToBoxset_Errors_ContributionAlreadyInBoxsetError e => e.Message,
                    IAddDiscToBoxset_AddDiscToBoxset_Errors_BoxsetNotFoundError e => e.Message,
                    IAddDiscToBoxset_AddDiscToBoxset_Errors_DiscNotFoundError e => e.Message,
                    IAddDiscToBoxset_AddDiscToBoxset_Errors_InvalidBoxsetStatusError e => e.Message,
                    IAddDiscToBoxset_AddDiscToBoxset_Errors_AuthenticationError e => e.Message,
                    IAddDiscToBoxset_AddDiscToBoxset_Errors_InvalidIdError e => e.Message,
                    IAddDiscToBoxset_AddDiscToBoxset_Errors_InvalidOwnershipError e => e.Message,
                    _ => $"An unexpected error occurred ({error.Code})"
                };
                return;
            }

            addedDiscIds.Add(discId);
            successMessage = "Disc added to boxset.";
        }
        catch (Exception)
        {
            errorMessage = "Failed to add disc. Please try again.";
        }
        finally
        {
            addingDiscIds.Remove(discId);
        }
    }
}
