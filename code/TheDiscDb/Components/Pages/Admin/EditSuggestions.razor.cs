using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Components.QuickGrid;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.JSInterop;
using TheDiscDb.Services.EditSuggestions;
using TheDiscDb.Web.Data;

namespace TheDiscDb.Components.Pages.Admin;

[Authorize(Roles = DefaultRoles.Administrator)]
public partial class EditSuggestions : ComponentBase, IAsyncDisposable
{
    [Inject]
    private IDbContextFactory<SqlServerDataContext> DbFactory { get; set; } = null!;

    [Inject]
    private NavigationManager Navigation { get; set; } = null!;

    [Inject]
    private IEditSuggestionBulkReviewService BulkReview { get; set; } = null!;

    [Inject]
    private AuthenticationStateProvider AuthStateProvider { get; set; } = null!;

    [Inject]
    private UserManager<TheDiscDbUser> UserManager { get; set; } = null!;

    [Inject]
    private ILogger<EditSuggestions> Logger { get; set; } = null!;

    [Inject]
    private IJSRuntime JS { get; set; } = null!;

    // Persisted in the query string so the chosen status survives navigation away and back.
    [SupplyParameterFromQuery(Name = "status")]
    public string? StatusQuery { get; set; }

    private List<EditSuggestion>? suggestions;
    private readonly EditSuggestionStatus[] statusList = Enum.GetValues<EditSuggestionStatus>();
    private EditSuggestionStatus selectedStatus = EditSuggestionStatus.Pending;
    private EditSuggestionStatus? loadedStatus;
    private QuickGrid<EditSuggestion>? grid;
    private ElementReference selectionContainer;
    private IJSObjectReference? selectionModule;
    private IJSObjectReference? rowSelection;
    private DotNetObjectReference<EditSuggestions>? selectionReference;
    private readonly SuggestionSelection selection = new();
    private bool shiftPressed;
    private bool showConfirmation;
    private bool rejecting;
    private bool isProcessing;
    private bool refreshFailed;
    private string rejectionReason = string.Empty;
    private int[] confirmedIds = [];
    private bool includesAppliedChanges;
    private string? actionError;
    private IReadOnlyList<BulkReviewResult> results = [];
    private bool ControlsDisabled => isProcessing || showConfirmation || refreshFailed;
    private bool CanApprove => selection.Selected.Count > 0 &&
        suggestions!.Where(s => selection.Selected.Contains(s.Id))
            .All(s => s.Changes.Any(c => c.Status == EditSuggestionChangeStatus.Pending));

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
        if (ControlsDisabled)
        {
            return;
        }

        // Update the URL; OnParametersSetAsync re-reads the query and refreshes the list.
        this.Navigation.NavigateTo(
            this.Navigation.GetUriWithQueryParameter("status", newStatus.ToString()));
    }

    private async Task RefreshList()
    {
        selection.Clear();
        await using var db = await DbFactory.CreateDbContextAsync();
        suggestions = await db.EditSuggestions
            .AsNoTracking()
            .Include(s => s.Changes)
            .Where(s => s.Status == selectedStatus)
            .OrderByDescending(s => s.Created)
            .ThenBy(s => s.Id)
            .ToListAsync();
        refreshFailed = false;
        if (suggestions.Count == 0)
        {
            grid = null;
        }
        else if (grid is not null)
        {
            await grid.RefreshDataAsync();
        }
    }

    private ValueTask<GridItemsProviderResult<EditSuggestion>> LoadRows(
        GridItemsProviderRequest<EditSuggestion> request)
    {
        var ordered = request.ApplySorting((suggestions ?? []).AsQueryable()).ToList();
        selection.SetOrder(ordered.Where(s => s.Status.IsReviewable()).Select(s => s.Id));
        return ValueTask.FromResult(GridItemsProviderResult.From(
            ordered.Skip(request.StartIndex).Take(request.Count ?? ordered.Count).ToList(),
            ordered.Count));
    }

    private void RecordModifiers(MouseEventArgs args) => shiftPressed = args.ShiftKey;

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (firstRender)
        {
            try
            {
                selectionModule = await JS.InvokeAsync<IJSObjectReference>("import", "./js/edit-suggestion-selection.js");
                selectionReference = DotNetObjectReference.Create(this);
                rowSelection = await selectionModule.InvokeAsync<IJSObjectReference>(
                    "initialize", selectionContainer, selectionReference);
            }
            catch (JSException ex)
            {
                Logger.LogError(ex, "Failed to enable edit suggestion row selection");
                actionError = "Row selection could not be enabled. You can still select suggestions using the checkboxes.";
                StateHasChanged();
            }
        }
    }

    [JSInvokable]
    public Task SelectSuggestionRow(int id, bool additive, bool shift) => InvokeAsync(() =>
    {
        if (!ControlsDisabled && selectedStatus.IsReviewable())
        {
            selection.SelectRow(id, additive, shift);
            StateHasChanged();
        }
    });

    private string GetRowClass(EditSuggestion suggestion) => !suggestion.Status.IsReviewable()
        ? string.Empty
        : selection.Selected.Contains(suggestion.Id)
            ? "selectable-suggestion selected-suggestion"
            : "selectable-suggestion";

    private void SelectRow(int id, bool selected)
    {
        if (!ControlsDisabled)
        {
            selection.SetSelected(id, selected, shiftPressed);
        }

        shiftPressed = false;
    }

    private void OpenConfirmation(bool reject)
    {
        if (ControlsDisabled || selection.Selected.Count == 0 || (!reject && !CanApprove))
        {
            return;
        }

        confirmedIds = selection.Order.Where(selection.Selected.Contains).ToArray();
        includesAppliedChanges = suggestions!
            .Where(s => selection.Selected.Contains(s.Id))
            .Any(s => s.Changes.Any(c => c.Status is EditSuggestionChangeStatus.Applied
                or EditSuggestionChangeStatus.Approved));
        rejecting = reject;
        rejectionReason = string.Empty;
        actionError = null;
        showConfirmation = true;
    }

    private void CancelConfirmation()
    {
        if (!isProcessing)
        {
            showConfirmation = false;
            confirmedIds = [];
        }
    }

    private async Task ConfirmReview()
    {
        if (isProcessing || !showConfirmation || confirmedIds.Length == 0 ||
            (rejecting && string.IsNullOrWhiteSpace(rejectionReason)))
        {
            return;
        }

        isProcessing = true;
        actionError = null;
        results = [];
        try
        {
            var authState = await AuthStateProvider.GetAuthenticationStateAsync();
            var userId = UserManager.GetUserId(authState.User)
                ?? throw new InvalidOperationException("Admin user ID could not be resolved.");
            results = rejecting
                ? await BulkReview.RejectAsync(confirmedIds, userId, rejectionReason)
                : await BulkReview.ApproveAsync(confirmedIds, userId);
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Failed to process selected edit suggestions");
            actionError = "Bulk processing could not finish. Some changes may have been saved. Review the details before retrying.";
        }
        finally
        {
            showConfirmation = false;
            confirmedIds = [];
            selection.Clear();
            try
            {
                await RefreshList();
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, "Failed to refresh edit suggestions after bulk review");
                refreshFailed = true;
                actionError = $"{actionError} The list could not be refreshed after processing. Reload the page before reviewing more suggestions.".Trim();
            }

            isProcessing = false;
        }
    }

    private static string GetOutcomeLabel(BulkReviewOutcome outcome) =>
        outcome == BulkReviewOutcome.NeedsReview ? "Needs review" : outcome.ToString();

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

    public async ValueTask DisposeAsync()
    {
        try
        {
            if (rowSelection is not null)
            {
                await rowSelection.InvokeVoidAsync("dispose");
                await rowSelection.DisposeAsync();
            }

            if (selectionModule is not null)
            {
                await selectionModule.DisposeAsync();
            }
        }
        catch (JSDisconnectedException ex)
        {
            Logger.LogDebug(ex, "Circuit disconnected while disposing edit suggestion row selection");
        }
        finally
        {
            selectionReference?.Dispose();
        }
    }
}

internal sealed class SuggestionSelection
{
    private readonly HashSet<int> selected = [];
    private int? anchor;
    public IReadOnlySet<int> Selected => selected;
    public IReadOnlyList<int> Order { get; private set; } = [];

    public void SetOrder(IEnumerable<int> ids)
    {
        var order = ids.ToArray();
        if (!Order.SequenceEqual(order))
        {
            anchor = null;
        }

        Order = order;
        selected.IntersectWith(order);
    }

    public void SetSelected(int id, bool value, bool shift)
    {
        var order = Order.ToList();
        var index = order.IndexOf(id);
        if (index < 0)
        {
            return;
        }

        var anchorIndex = anchor is int anchorId ? order.IndexOf(anchorId) : -1;
        if (shift && anchorIndex >= 0)
        {
            selected.UnionWith(order.Skip(Math.Min(index, anchorIndex)).Take(Math.Abs(index - anchorIndex) + 1));
            return;
        }

        if (value)
        {
            selected.Add(id);
        }
        else
        {
            selected.Remove(id);
        }

        anchor = id;
    }

    public void SelectRow(int id, bool additive, bool shift)
    {
        if (!Order.Contains(id))
        {
            return;
        }

        if (!additive && !shift)
        {
            selected.Clear();
            SetSelected(id, true, false);
        }
        else
        {
            SetSelected(id, !selected.Contains(id), shift);
        }
    }

    public void Clear()
    {
        selected.Clear();
        anchor = null;
    }
}