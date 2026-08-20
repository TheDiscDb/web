namespace TheDiscDb.Components.Pages.Admin;

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.QuickGrid;
using TheDiscDb.Services.Admin;
using TheDiscDb.Web.Data;

[Authorize(Roles = DefaultRoles.Administrator)]
public partial class Intake : ComponentBase
{
    [Inject]
    private IIntakeAdminService IntakeAdminService { get; set; } = null!;

    [Inject]
    private NavigationManager Navigation { get; set; } = null!;

    [SupplyParameterFromQuery(Name = "q")]
    public string? Query { get; set; }

    [SupplyParameterFromQuery(Name = "status")]
    public string? StatusQuery { get; set; }

    private readonly IntakeStatus[] statuses = Enum.GetValues<IntakeStatus>();
    private readonly PaginationState pagination = new() { ItemsPerPage = 25 };
    private QuickGrid<IntakeReleaseListItem>? grid;
    private string? searchText;
    private IntakeStatus? selectedStatus;
    // Keep the applied filters separate so refreshes use the current URL state, not stale query parameters.
    private string? appliedSearchText;
    private IntakeStatus? appliedStatus;

    protected override void OnParametersSet()
    {
        IntakeStatus? status = Enum.TryParse<IntakeStatus>(
            StatusQuery,
            ignoreCase: true,
            out IntakeStatus parsedStatus)
            ? parsedStatus
            : null;
        searchText = Query;
        selectedStatus = status;
        appliedSearchText = Query;
        appliedStatus = status;
    }

    private async ValueTask<GridItemsProviderResult<IntakeReleaseListItem>> LoadReleasesAsync(
        GridItemsProviderRequest<IntakeReleaseListItem> request)
    {
        int pageSize = request.Count ?? 25;
        var result = await IntakeAdminService.GetReleasesAsync(
            new IntakeReleaseSearch(appliedSearchText, appliedStatus, request.StartIndex / pageSize, pageSize),
            request.CancellationToken);
        return GridItemsProviderResult.From<IntakeReleaseListItem>(result.Items.ToList(), result.TotalCount);
    }

    private async Task ApplySearchAsync()
    {
        var queryText = string.IsNullOrWhiteSpace(searchText) ? null : searchText.Trim();
        var status = selectedStatus;
        searchText = queryText;
        selectedStatus = status;
        appliedSearchText = queryText;
        appliedStatus = status;
        Query = queryText;
        StatusQuery = status?.ToString();

        var query = new List<string>();
        if (!string.IsNullOrEmpty(queryText))
        {
            query.Add($"q={Uri.EscapeDataString(queryText)}");
        }

        if (status is { } selectedStatusValue)
        {
            query.Add($"status={Uri.EscapeDataString(selectedStatusValue.ToString())}");
        }

        await pagination.SetCurrentPageIndexAsync(0);
        Navigation.NavigateTo($"/admin/intake{(query.Count == 0 ? string.Empty : "?" + string.Join("&", query))}", replace: true);
        if (grid != null)
        {
            await grid.RefreshDataAsync();
        }
    }

    private static (string Text, string Url) GetRootAdminLink() => ("Admin", "/admin");
}
