using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Components;
using StrawberryShake;
using TheDiscDb.Client.Contributions;
using TheDiscDb.Services;

namespace TheDiscDb.Client.Pages.Contribute;

[Authorize]
public partial class EditDisc : ComponentBase
{
    [Parameter]
    public string? ContributionId { get; set; }

    [Parameter]
    public string? DiscId { get; set; }

    [SupplyParameterFromQuery(Name = "returnUrl")]
    public string? ReturnUrl { get; set; }

    [Inject]
    public IContributionClient ContributionClient { get; set; } = default!;

    [Inject]
    public NavigationManager Navigation { get; set; } = default!;

    private readonly SaveDiscRequest request = new();
    private readonly string[] formats = ["4K", "Blu-ray", "DVD"];

    private bool isLoading = true;
    private bool discFound;
    private string? errorMessage;

    protected override async Task OnInitializedAsync()
    {
        var result = await this.ContributionClient.GetDisc.ExecuteAsync(this.ContributionId!, this.DiscId!);
        if (result?.Data?.MyContributions?.Nodes != null && result.IsSuccessResult())
        {
            var contribution = result.Data.MyContributions.Nodes.FirstOrDefault();
            if (contribution != null)
            {
                var disc = contribution.Discs.FirstOrDefault();
                if (disc != null)
                {
                    this.request.ContentHash = disc.ContentHash;
                    this.request.Name = disc.Name;
                    this.request.Slug = disc.Slug;
                    this.request.Format = disc.Format;
                    this.discFound = true;
                }
            }
        }

        if (!this.discFound)
        {
            this.errorMessage = "Disc not found.";
        }

        this.isLoading = false;
    }

    async Task HandleValidSubmit()
    {
        var response = await this.ContributionClient.UpdateDisc.ExecuteAsync(new UpdateDiscInput
        {
            ContributionId = this.ContributionId!,
            DiscId = this.DiscId!,
            Format = request.Format,
            Name = request.Name,
            Slug = request.Slug
        });

        if (response.IsSuccessResult())
        {
            var returnUrl = !string.IsNullOrEmpty(this.ReturnUrl)
                ? this.ReturnUrl
                : $"/contribution/{this.ContributionId}/discs/{this.DiscId}";
            this.Navigation!.NavigateTo(returnUrl);
        }
    }

    private void DiscTitleChanged(ChangeEventArgs args)
    {
        if (args?.Value != null)
        {
            string title = args.Value.ToString()!;

            if (!string.IsNullOrEmpty(title))
            {
                this.request.Slug = title.Slugify();
            }
        }
    }
}
