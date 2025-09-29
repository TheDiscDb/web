using MakeMkv;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Components;

namespace TheDiscDb.Client.Pages.Contribute;

[Authorize]
public partial class IdentifyDiscItems : ComponentBase
{
    [Parameter]
    public string? ContributionId { get; set; }

    [Parameter]
    public string? DiscId { get; set; }

    [Inject]
    private NavigationManager NavigationManager { get; set; } = null!;

    [Inject]
    private ApiClient Client { get; set; } = null!;

    private IQueryable<MakeMkv.Title>? titles = null;

    protected override async Task OnInitializedAsync()
    {
        var logs = await this.Client.GetDiscLogsAsync(this.ContributionId!, this.DiscId!);
        var lines = logs.Content.Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var parsed = LogParser.Parse(lines);
        this.titles = LogParser.Organize(parsed).Titles.AsQueryable();
    }
}
