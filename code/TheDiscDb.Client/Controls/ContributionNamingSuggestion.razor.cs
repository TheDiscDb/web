using Microsoft.AspNetCore.Components;

namespace TheDiscDb.Client.Controls;

public partial class ContributionNamingSuggestion
{
    [Parameter]
    public required string EntityName { get; set; }

    [Parameter]
    public required string NameLabel { get; set; }

    [Parameter]
    public string? SuggestedName { get; set; }

    [Parameter]
    public string? SuggestedSlug { get; set; }

    [Parameter]
    public EventCallback OnApply { get; set; }

    private bool HasSuggestion =>
        !string.IsNullOrWhiteSpace(this.SuggestedName) &&
        !string.IsNullOrWhiteSpace(this.SuggestedSlug);
}
