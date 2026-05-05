using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Components;
using StrawberryShake;
using TheDiscDb.Client.Contributions;

namespace TheDiscDb.Client.Pages.Contribute;

[Authorize]
public partial class MyContributions : CancellableComponentBase
{
    private const int DefaultPageSize = 50;
    // Server caps individual requests at 100 (UsePaging MaxPageSize). We follow cursors
    // up to MaxItemsPerSource per source to keep the client-side merge bounded. Documented
    // as a future scaling concern in the plan — server-side union pagination is the long-term
    // fix when individual users routinely have more than this many of either type.
    private const int ServerPageSize = 100;
    private const int MaxItemsPerSource = 500;

    private static readonly UserContributionStatus[] DeletableStatuses =
    [
        UserContributionStatus.Pending,
        UserContributionStatus.Rejected,
        UserContributionStatus.ChangesRequested
    ];

    [Inject]
    GetCurrentUserContributionsQuery ContributionsQuery { get; set; } = null!;

    [Inject]
    GetCurrentUserBoxsetsQuery BoxsetsQuery { get; set; } = null!;

    [Inject]
    NavigationManager Navigation { get; set; } = null!;

    [Inject]
    IContributionClient ContributionClient { get; set; } = null!;

    [Parameter]
    [SupplyParameterFromQuery(Name = "status")]
    public string? StatusFilter { get; set; }

    [Parameter]
    [SupplyParameterFromQuery(Name = "page")]
    public int? Page { get; set; }

    [Parameter]
    [SupplyParameterFromQuery(Name = "count")]
    public int? Count { get; set; }

    public IQueryable<MyItemRow>? Items { get; set; }

    public int CurrentPage { get; private set; } = 1;
    public int PageSize { get; private set; } = DefaultPageSize;
    public int TotalCount { get; private set; }
    public int TotalPages => PageSize > 0 ? (int)Math.Ceiling((double)TotalCount / PageSize) : 1;
    public bool HasNextPage => CurrentPage < TotalPages;
    public bool HasPreviousPage => CurrentPage > 1;

    // Full merged list, kept in memory so paging doesn't refetch.
    private List<MyItemRow> mergedRows = new();
    // Monotonically increasing token used to discard stale loads when the filter changes mid-flight.
    private int loadSequence;

    private bool showDeleteDialog;
    private bool isDeleting;
    private string? deleteErrorMessage;
    private MyItemRow? deletingItem;

    protected override async Task OnInitializedAsync()
    {
        CurrentPage = Page is > 0 ? Page.Value : 1;
        PageSize = Count is > 0 ? Count.Value : DefaultPageSize;

        await LoadAsync();

        // Clamp page to available range after load
        if (CurrentPage > TotalPages)
        {
            CurrentPage = Math.Max(1, TotalPages);
        }
        ApplyPaging();
    }

    private async Task OnStatusFilterChanged(ChangeEventArgs e)
    {
        var selected = e.Value?.ToString();
        StatusFilter = string.IsNullOrEmpty(selected) ? null : selected;

        CurrentPage = 1;
        UpdateUrl();
        await LoadAsync();
        ApplyPaging();
    }

    public Task GoToNextPage()
    {
        if (!HasNextPage) return Task.CompletedTask;
        CurrentPage++;
        UpdateUrl();
        ApplyPaging();
        return Task.CompletedTask;
    }

    public Task GoToPreviousPage()
    {
        if (!HasPreviousPage) return Task.CompletedTask;
        CurrentPage--;
        UpdateUrl();
        ApplyPaging();
        return Task.CompletedTask;
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

    private async Task LoadAsync()
    {
        var thisLoad = ++loadSequence;

        UserContributionFilterInput? contribFilter = null;
        UserContributionBoxsetFilterInput? boxsetFilter = null;
        if (TryGetNormalizedStatus(out var normalizedStatus))
        {
            contribFilter = new UserContributionFilterInput
            {
                Status = new UserContributionStatusOperationFilterInput { Eq = normalizedStatus!.Value }
            };
            boxsetFilter = new UserContributionBoxsetFilterInput
            {
                Status = new UserContributionStatusOperationFilterInput { Eq = normalizedStatus.Value }
            };
        }

        // Fetch both sources in parallel, each following cursors up to MaxItemsPerSource.
        var contribTask = FetchAllContributionsAsync(contribFilter);
        var boxsetTask = FetchAllBoxsetsAsync(boxsetFilter);
        await Task.WhenAll(contribTask, boxsetTask);

        // Discard stale loads (filter changed before we finished).
        if (thisLoad != loadSequence) return;

        var rows = new List<MyItemRow>();
        rows.AddRange(await contribTask);
        rows.AddRange(await boxsetTask);

        mergedRows = rows.OrderByDescending(r => r.Created).ToList();
        TotalCount = mergedRows.Count;
    }

    private async Task<List<MyItemRow>> FetchAllContributionsAsync(UserContributionFilterInput? filter)
    {
        var rows = new List<MyItemRow>();
        string? after = null;
        while (rows.Count < MaxItemsPerSource)
        {
            var pageSize = Math.Min(ServerPageSize, MaxItemsPerSource - rows.Count);
            var result = await ContributionsQuery.ExecuteAsync(filter, pageSize, after);
            if (result == null || !result.IsSuccessResult()) break;
            var connection = result.Data?.MyContributions;
            var nodes = connection?.Nodes;
            if (nodes != null)
            {
                foreach (var c in nodes)
                {
                    rows.Add(new MyItemRow(
                        EncodedId: c.EncodedId,
                        Name: !string.IsNullOrEmpty(c.ReleaseTitle) ? c.ReleaseTitle : (c.Title ?? "(untitled)"),
                        Title: c.Title ?? string.Empty,
                        Type: NormalizeMediaType(c.MediaType),
                        Status: c.Status,
                        Created: c.Created,
                        IsBoxset: false,
                        DetailUrl: $"/contribution/{c.EncodedId}"));
                }
            }
            if (connection?.PageInfo.HasNextPage != true) break;
            after = connection.PageInfo.EndCursor;
            if (string.IsNullOrEmpty(after)) break;
        }
        return rows;
    }

    private async Task<List<MyItemRow>> FetchAllBoxsetsAsync(UserContributionBoxsetFilterInput? filter)
    {
        var rows = new List<MyItemRow>();
        string? after = null;
        while (rows.Count < MaxItemsPerSource)
        {
            var pageSize = Math.Min(ServerPageSize, MaxItemsPerSource - rows.Count);
            var result = await BoxsetsQuery.ExecuteAsync(filter, pageSize, after);
            if (result == null || !result.IsSuccessResult()) break;
            var connection = result.Data?.MyBoxsets;
            var nodes = connection?.Nodes;
            if (nodes != null)
            {
                foreach (var b in nodes)
                {
                    rows.Add(new MyItemRow(
                        EncodedId: b.EncodedId,
                        Name: b.Title,
                        Title: b.Title,
                        Type: "Boxset",
                        Status: b.Status,
                        Created: b.Created,
                        IsBoxset: true,
                        DetailUrl: $"/contribution/boxset/{b.EncodedId}"));
                }
            }
            if (connection?.PageInfo.HasNextPage != true) break;
            after = connection.PageInfo.EndCursor;
            if (string.IsNullOrEmpty(after)) break;
        }
        return rows;
    }

    private void ApplyPaging()
    {
        var skip = (CurrentPage - 1) * PageSize;
        Items = mergedRows.Skip(skip).Take(PageSize).AsQueryable();
    }

    private static string NormalizeMediaType(string? mediaType)
    {
        if (string.IsNullOrWhiteSpace(mediaType)) return "—";
        return mediaType.Equals("movie", StringComparison.OrdinalIgnoreCase) ? "Movie"
            : mediaType.Equals("series", StringComparison.OrdinalIgnoreCase) ? "Series"
            : mediaType;
    }

    private bool TryGetNormalizedStatus(out UserContributionStatus? normalizedStatus)
    {
        normalizedStatus = null;
        if (string.IsNullOrWhiteSpace(StatusFilter)) return false;
        if (!Enum.TryParse(StatusFilter.Trim(), ignoreCase: true, out UserContributionStatus parsedStatus)) return false;
        normalizedStatus = parsedStatus;
        return true;
    }

    private static bool CanDelete(MyItemRow row) => DeletableStatuses.Contains(row.Status);

    private void ConfirmDelete(MyItemRow row)
    {
        deletingItem = row;
        deleteErrorMessage = null;
        showDeleteDialog = true;
    }

    private void CancelDelete()
    {
        showDeleteDialog = false;
        deletingItem = null;
        deleteErrorMessage = null;
    }

    private async Task ExecuteDelete()
    {
        if (deletingItem == null) return;

        isDeleting = true;
        deleteErrorMessage = null;

        try
        {
            if (deletingItem.IsBoxset)
            {
                var result = await ContributionClient.DeleteBoxset.ExecuteAsync(new DeleteBoxsetInput
                {
                    BoxsetId = deletingItem.EncodedId
                });

                if (result.Data?.DeleteBoxset?.Errors is { Count: > 0 } errors)
                {
                    var error = errors[0];
                    deleteErrorMessage = error switch
                    {
                        IDeleteBoxset_DeleteBoxset_Errors_AuthenticationError e => e.Message,
                        IDeleteBoxset_DeleteBoxset_Errors_BoxsetNotFoundError e => e.Message,
                        IDeleteBoxset_DeleteBoxset_Errors_InvalidIdError e => e.Message,
                        IDeleteBoxset_DeleteBoxset_Errors_InvalidOwnershipError e => e.Message,
                        _ => $"An unexpected error occurred ({error.Code})"
                    };
                    return;
                }
            }
            else
            {
                var result = await ContributionClient.DeleteContribution.ExecuteAsync(new DeleteContributionInput
                {
                    ContributionId = deletingItem.EncodedId
                });

                if (result.Data?.DeleteContribution?.Errors is { Count: > 0 } errors)
                {
                    var error = errors[0];
                    deleteErrorMessage = error switch
                    {
                        IDeleteContribution_DeleteContribution_Errors_InvalidContributionStatusError e => e.Message,
                        IDeleteContribution_DeleteContribution_Errors_ContributionNotFoundError e => e.Message,
                        IDeleteContribution_DeleteContribution_Errors_InvalidOwnershipError e => e.Message,
                        IDeleteContribution_DeleteContribution_Errors_AuthenticationError e => e.Message,
                        IDeleteContribution_DeleteContribution_Errors_InvalidIdError e => e.Message,
                        _ => $"An unexpected error occurred ({error.Code})"
                    };
                    return;
                }
            }

            showDeleteDialog = false;
            deletingItem = null;

            CurrentPage = 1;
            await LoadAsync();
            ApplyPaging();
        }
        catch (Exception)
        {
            deleteErrorMessage = "An unexpected error occurred. Please try again.";
        }
        finally
        {
            isDeleting = false;
        }
    }

    public record MyItemRow(
        string EncodedId,
        string Name,
        string Title,
        string Type,
        UserContributionStatus Status,
        DateTimeOffset Created,
        bool IsBoxset,
        string DetailUrl);
}
