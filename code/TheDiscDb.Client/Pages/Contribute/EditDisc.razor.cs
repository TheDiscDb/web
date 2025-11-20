using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Components;
using TheDiscDb.Services;

namespace TheDiscDb.Client.Pages.Contribute;

[Authorize]
public partial class EditDisc : ComponentBase
{
    [Parameter]
    public string? ContributionId { get; set; }

    [Parameter]
    public string? DiscId { get; set; }

    [Inject]
    public IUserContributionService Client { get; set; } = default!;

    [Inject]
    public NavigationManager Navigation { get; set; } = default!;

    private readonly SaveDiscRequest request = new SaveDiscRequest
    {
    };
    readonly string[] formats = [ "4K", "Blu-ray", "DVD" ];

    protected override async Task OnInitializedAsync()
    {
        var disc = await this.Client.GetDisc(this.ContributionId!, this.DiscId!);
        if (disc.IsSuccess)
        {
            this.request.ContentHash = disc.Value.ContentHash;
            this.request.Name = disc.Value.Name;
            this.request.Slug = disc.Value.Slug;
            this.request.Format = disc.Value.Format;
        }
    }

    async Task HandleValidSubmit()
    {
        var response = await this.Client.UpdateDisc(this.ContributionId!, this.DiscId!, this.request);
        if (response.IsSuccess)
        {
            this.Navigation!.NavigateTo($"/contribution/{this.ContributionId}/discs/{this.DiscId}");
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
