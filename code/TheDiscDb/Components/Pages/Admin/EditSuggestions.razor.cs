using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Components;
using Microsoft.EntityFrameworkCore;
using TheDiscDb.Web.Data;

namespace TheDiscDb.Components.Pages.Admin;

[Authorize(Roles = DefaultRoles.Administrator)]
public partial class EditSuggestions : ComponentBase
{
    [Inject]
    private IDbContextFactory<SqlServerDataContext> DbFactory { get; set; } = null!;

    private List<EditSuggestion>? suggestions;
    private readonly EditSuggestionStatus[] statusList = Enum.GetValues<EditSuggestionStatus>();
    private EditSuggestionStatus selectedStatus = EditSuggestionStatus.Pending;

    protected override async Task OnInitializedAsync()
    {
        await RefreshList();
    }

    private async Task OnStatusChanged(EditSuggestionStatus newStatus)
    {
        selectedStatus = newStatus;
        await RefreshList();
    }

    private async Task RefreshList()
    {
        await using var db = await DbFactory.CreateDbContextAsync();
        suggestions = await db.EditSuggestions
            .AsNoTracking()
            .Include(s => s.Changes)
            .Where(s => s.Status == selectedStatus)
            .OrderByDescending(s => s.Created)
            .ToListAsync();
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

    private static (string Text, string Url) GetRootAdminLink() => ("Admin", "/admin");
}
