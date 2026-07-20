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

    [Inject]
    private NavigationManager Navigation { get; set; } = null!;

    // Persisted in the query string so the chosen status survives navigation away and back.
    [SupplyParameterFromQuery(Name = "status")]
    public string? StatusQuery { get; set; }

    private List<EditSuggestion>? suggestions;
    private readonly EditSuggestionStatus[] statusList = Enum.GetValues<EditSuggestionStatus>();
    private EditSuggestionStatus selectedStatus = EditSuggestionStatus.Pending;
    private EditSuggestionStatus? loadedStatus;

    protected override async Task OnParametersSetAsync()
    {
        selectedStatus = Enum.TryParse<EditSuggestionStatus>(this.StatusQuery, ignoreCase: true, out var parsed)
            ? parsed
            : EditSuggestionStatus.Pending;

        // Only hit the database when the effective status actually changed (query navigation or
        // first load), not on every re-render.
        if (this.loadedStatus != selectedStatus)
        {
            this.loadedStatus = selectedStatus;
            await RefreshList();
        }
    }

    private void OnStatusChanged(EditSuggestionStatus newStatus)
    {
        // Update the URL; OnParametersSetAsync re-reads the query and refreshes the list.
        this.Navigation.NavigateTo(
            this.Navigation.GetUriWithQueryParameter("status", newStatus.ToString()));
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
