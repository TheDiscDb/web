namespace TheDiscDb.Components.Controls;

using Microsoft.AspNetCore.Components;

public partial class EditSuggestionTimeline : ComponentBase
{
    [Parameter]
    public IReadOnlyList<EditSuggestionTimelineEntry> Entries { get; set; } = [];
}

public sealed record EditSuggestionTimelineEntry(
    string Label,
    string BadgeClass,
    DateTimeOffset Timestamp,
    string Actor,
    string Content);
