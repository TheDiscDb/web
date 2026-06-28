using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Identity;
using TheDiscDb.Services.EditSuggestions;
using TheDiscDb.Services.Server;
using TheDiscDb.Web.Data;

namespace TheDiscDb.Components.Pages;

[Authorize]
public partial class MyChanges : AuthenticatedComponentBase
{
    [Inject]
    private IEditSuggestionService SuggestionService { get; set; } = null!;

    [Inject]
    private IdEncoder IdEncoder { get; set; } = null!;

    private IReadOnlyList<EditSuggestion>? suggestions;
    private EditSuggestionStatus? selectedStatus;

    private readonly List<StatusOption> statusOptions =
    [
        new("All", null),
        ..Enum.GetValues<EditSuggestionStatus>().Select(s => new StatusOption(s.ToString(), s))
    ];

    protected override async Task OnInitializedAsync()
    {
        await RefreshList();
    }

    private async Task OnStatusChanged(EditSuggestionStatus? newStatus)
    {
        selectedStatus = newStatus;
        await RefreshList();
    }

    private async Task RefreshList()
    {
        var userId = await GetCurrentUserIdAsync();
        if (userId == null) return;

        var filter = new EditSuggestionListFilter
        {
            UserId = userId,
            MineOnly = true,
            Statuses = selectedStatus.HasValue ? [selectedStatus.Value] : null
        };

        suggestions = await SuggestionService.ListAsync(filter, CancellationToken.None);
    }

    private static string GetStatusBadge(EditSuggestionStatus status) => status switch
    {
        EditSuggestionStatus.Pending => "bg-warning text-dark",
        EditSuggestionStatus.InReview => "bg-info text-dark",
        EditSuggestionStatus.Approved => "bg-success",
        EditSuggestionStatus.PartiallyApproved => "bg-success",
        EditSuggestionStatus.Rejected => "bg-danger",
        EditSuggestionStatus.Conflicted => "bg-danger",
        EditSuggestionStatus.Withdrawn => "bg-secondary",
        _ => "bg-light text-dark",
    };

    public sealed record StatusOption(string Label, EditSuggestionStatus? Value);
}
