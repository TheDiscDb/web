using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Components;
using StrawberryShake;
using TheDiscDb.Client.Contributions;

namespace TheDiscDb.Client.Pages.Contribute;

[Authorize]
public partial class MyContributions : ComponentBase
{
    private const int DefaultPageSize = 50;

    [Inject]
    GetCurrentUserContributionsQuery Query { get; set; } = null!;

    [Inject]
    NavigationManager Navigation { get; set; } = null!;

    [Parameter]
    [SupplyParameterFromQuery(Name = "status")]
    public string? StatusFilter { get; set; }

    [Parameter]
    [SupplyParameterFromQuery(Name = "page")]
    public int? Page { get; set; }

    [Parameter]
    [SupplyParameterFromQuery(Name = "count")]
    public int? Count { get; set; }

    public IQueryable<IGetCurrentUserContributions_MyContributions_Nodes>? Contributions { get; set; }

    public int CurrentPage { get; private set; } = 1;
    public int PageSize { get; private set; } = DefaultPageSize;
    public int TotalCount { get; private set; }
    public int TotalPages => PageSize > 0 ? (int)Math.Ceiling((double)TotalCount / PageSize) : 1;
    public bool HasNextPage => CurrentPage < TotalPages;
    public bool HasPreviousPage => CurrentPage > 1;

    // Store the end cursor from each page so forward navigation doesn't re-fetch.
    // Key = page number that this cursor leads to (i.e., page 2's entry = endCursor from page 1).
    private readonly Dictionary<int, string?> endCursors = new() { { 1, null } };

    protected override async Task OnInitializedAsync()
    {
        CurrentPage = Page is > 0 ? Page.Value : 1;
        PageSize = Count is > 0 ? Count.Value : DefaultPageSize;

        // If deep-linking to page > 1, start at page 1 since we don't have the cursor
        if (CurrentPage > 1 && !endCursors.ContainsKey(CurrentPage))
        {
            CurrentPage = 1;
        }

        await LoadContributionsAsync();
    }

    private async Task OnStatusFilterChanged(ChangeEventArgs e)
    {
        var selected = e.Value?.ToString();
        StatusFilter = string.IsNullOrEmpty(selected) ? null : selected;

        CurrentPage = 1;
        endCursors.Clear();
        endCursors[1] = null;

        UpdateUrl();
        await LoadContributionsAsync();
    }

    public async Task GoToNextPage()
    {
        if (!HasNextPage)
            return;

        CurrentPage++;
        UpdateUrl();
        await LoadContributionsAsync();
    }

    public async Task GoToPreviousPage()
    {
        if (!HasPreviousPage)
            return;

        CurrentPage--;
        UpdateUrl();
        await LoadContributionsAsync();
    }

    private void UpdateUrl()
    {
        var uri = Navigation.GetUriWithQueryParameters(new Dictionary<string, object?>
        {
            ["status"] = string.IsNullOrEmpty(StatusFilter) ? null : StatusFilter,
            ["page"] = CurrentPage > 1 ? CurrentPage : null,
            ["count"] = PageSize != DefaultPageSize ? PageSize : null
        });
        Navigation.NavigateTo(uri);
    }

    private async Task LoadContributionsAsync()
    {
        UserContributionFilterInput? input = null;
        if (TryGetNormalizedStatus(out var normalizedStatus))
        {
            input = new UserContributionFilterInput
            {
                Status = new UserContributionStatusOperationFilterInput
                {
                    Eq = normalizedStatus!.Value
                }
            };
        }

        var afterCursor = endCursors.GetValueOrDefault(CurrentPage);
        var results = await Query.ExecuteAsync(input, PageSize, afterCursor);
        if (results != null && results.IsSuccessResult())
        {
            var connection = results.Data?.MyContributions;
            var nodes = connection?.Nodes ?? Array.Empty<IGetCurrentUserContributions_MyContributions_Nodes>();
            Contributions = nodes.AsQueryable();
            TotalCount = connection?.TotalCount ?? 0;

            // Cache the end cursor so the next page can be fetched without re-querying
            if (connection?.PageInfo.EndCursor != null)
            {
                endCursors[CurrentPage + 1] = connection.PageInfo.EndCursor;
            }
        }
    }

    private bool TryGetNormalizedStatus(out UserContributionStatus? normalizedStatus)
    {
        normalizedStatus = null;

        if (string.IsNullOrWhiteSpace(StatusFilter))
        {
            return false;
        }

        if (!Enum.TryParse(StatusFilter.Trim(), ignoreCase: true, out UserContributionStatus parsedStatus))
        {
            return false;
        }

        normalizedStatus = parsedStatus;
        return true;
    }
}
