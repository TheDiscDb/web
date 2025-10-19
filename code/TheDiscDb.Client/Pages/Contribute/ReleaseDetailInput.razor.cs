using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Components;
using TheDiscDb.Services;

namespace TheDiscDb.Client.Pages.Contribute;

[Authorize]
public partial class ReleaseDetailInput : ComponentBase
{
    [Parameter]
    public string? Hash { get; set; }

    [Parameter]
    public string? MediaType { get; set; }

    [Parameter]
    public string? ExternalId { get; set; }

    [Inject]
    public IUserContributionService Client { get; set; } = default!;

    [Inject]
    public NavigationManager NavigationManager { get; set; } = default!;

    private readonly CreateContributionRequest request = new();
    private string releaseDate = string.Empty;

    protected override Task OnInitializedAsync()
    {
        this.request.MediaType = this.MediaType ?? "Movie";
        this.request.DiscHash = this.Hash ?? string.Empty;
        this.request.ExternalProvider = "TMDB";
        this.request.ExternalId = this.ExternalId ?? string.Empty;
        return Task.CompletedTask;
    }

    async Task HandleValidSubmit()
    {
        var response = await this.Client.CreateContribution(userId: string.Empty, this.request);

        this.NavigationManager!.NavigateTo($"/contribution/{response.Value.ContributionId}");
    }

    private void ReleaseTitleChanged(ChangeEventArgs args)
    {
        if (args?.Value != null)
        {
            string title = args.Value.ToString()!;
            int? year = null;
            if (this.request.ReleaseDate.Year > 1980)
            {
                year = this.request.ReleaseDate.Year;
            }

            this.request.ReleaseSlug = CreateSlug(title, year);
        }
    }

    private void ReleaseDateChanged(ChangeEventArgs args)
    {
        if (args?.Value != null)
        {
            if (DateTimeOffset.TryParse(args.Value.ToString(), out var date))
            {
                this.request.ReleaseDate = date;
                this.releaseDate = date.ToString("MM-dd-yyyy");

                if (!string.IsNullOrEmpty(request.ReleaseTitle))
                {
                    int? year = null;
                    if (this.request.ReleaseDate.Year > 1980)
                    {
                        year = this.request.ReleaseDate.Year;
                    }

                    this.request.ReleaseSlug = CreateSlug(request.ReleaseTitle, year);
                }
                else
                {
                    this.request.ReleaseSlug = "";
                }
            }
        }
        else
        {
            this.releaseDate = string.Empty;
            this.request.ReleaseDate = DateTimeOffset.MinValue;
            if (!string.IsNullOrEmpty(request.ReleaseTitle))
            {
                this.request.ReleaseSlug = CreateSlug(request.ReleaseTitle, year: null);
            }
            else
            {
                this.request.ReleaseSlug = "";
            }
        }
    }

    private static string CreateSlug(string title, int? year)
    {
        string slug = title.Slugify();
        
        if (year.HasValue)
        {
            slug = $"{year.Value}-{slug}";
        }

        return slug;
    }
}
