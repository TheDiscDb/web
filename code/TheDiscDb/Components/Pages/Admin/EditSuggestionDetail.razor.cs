using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using TheDiscDb.Services.EditSuggestions;
using TheDiscDb.Web.Data;

namespace TheDiscDb.Components.Pages.Admin;

[Authorize(Roles = DefaultRoles.Administrator)]
public partial class EditSuggestionDetail : ComponentBase
{
    [Parameter]
    public int SuggestionId { get; set; }

    [Inject]
    private IDbContextFactory<SqlServerDataContext> DbFactory { get; set; } = null!;

    [Inject]
    private IEditSuggestionReviewService ReviewService { get; set; } = null!;

    [Inject]
    private AuthenticationStateProvider AuthStateProvider { get; set; } = null!;

    [Inject]
    private UserManager<TheDiscDbUser> UserManager { get; set; } = null!;

    [Inject]
    private ILogger<EditSuggestionDetail> Logger { get; set; } = null!;

    private EditSuggestion? suggestion;
    private string? actionMessage;
    private bool actionSuccess;
    private bool isProcessing;
    private string conflictResolution = string.Empty;
    private Dictionary<int, string> rejectionReasons = [];

    // Disc ID conflict resolution context + the admin's chosen destination release-disc per change.
    private Dictionary<int, DiscIdConflictContext> discIdConflicts = [];
    private Dictionary<int, int> selectedDestination = [];

    private static readonly IReadOnlyCollection<string> IdentityFields =
    [
        "mediaItemSlug", "boxsetSlug", "releaseSlug",
        "discSlug", "discIndex", "titleIndex",
        "chapterIndex", "trackIndex", "sourceFile", "hasItem",
    ];

    private static readonly JsonSerializerOptions IndentedJson = new() { WriteIndented = true };

    protected override async Task OnInitializedAsync()
    {
        await LoadSuggestion();
    }

    private async Task LoadSuggestion()
    {
        await using var db = await DbFactory.CreateDbContextAsync();
        suggestion = await db.EditSuggestions
            .Include(s => s.Changes.OrderBy(c => c.Ordinal))
            .FirstOrDefaultAsync(s => s.Id == SuggestionId);

        if (suggestion != null)
        {
            foreach (var change in suggestion.Changes)
            {
                rejectionReasons.TryAdd(change.Id, string.Empty);
            }

            // Load Disc ID conflict context (submitted id, target id, candidate release-discs) for
            // any conflicted disc.fields.update change so the admin can attribute the id correctly.
            discIdConflicts = [];
            foreach (var change in suggestion.Changes.Where(c =>
                c.Status == EditSuggestionChangeStatus.Conflicted && c.Type == "disc.fields.update"))
            {
                var context = await ReviewService.GetDiscIdConflictContextAsync(SuggestionId, change.Id);
                if (context is not null)
                {
                    discIdConflicts[change.Id] = context;
                    if (!selectedDestination.ContainsKey(change.Id))
                    {
                        // Default to the first candidate that currently has no id (a likely re-pressed sibling).
                        var preferred = context.Candidates.FirstOrDefault(c => string.IsNullOrEmpty(c.CurrentGlobalDiscId))
                            ?? context.Candidates.FirstOrDefault();
                        if (preferred is not null)
                        {
                            selectedDestination[change.Id] = preferred.ReleaseDiscId;
                        }
                    }
                }
            }
        }
    }

    private async Task ApproveChange(int changeId)
    {
        isProcessing = true;
        actionMessage = null;

        try
        {
            var userId = await GetAdminUserId();
            var result = await ReviewService.ApproveChangeAsync(SuggestionId, changeId, userId);
            if (result == null)
            {
                actionMessage = "Change not found or not in a pending state.";
                actionSuccess = false;
            }
            else if (result.Status == EditSuggestionChangeStatus.Conflicted)
            {
                actionMessage = $"Change #{changeId} has a conflict: {result.ConflictReason}";
                actionSuccess = false;
            }
            else
            {
                actionMessage = $"Change #{changeId} approved and applied.";
                actionSuccess = true;
            }

            await LoadSuggestion();
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Failed to approve change {ChangeId}", changeId);
            actionMessage = "An error occurred while approving the change.";
            actionSuccess = false;
        }
        finally
        {
            isProcessing = false;
        }
    }

    private async Task RejectChange(int changeId)
    {
        isProcessing = true;
        actionMessage = null;

        try
        {
            var userId = await GetAdminUserId();
            var reason = rejectionReasons.GetValueOrDefault(changeId);
            var result = await ReviewService.RejectChangeAsync(SuggestionId, changeId, userId, reason);
            if (result == null)
            {
                actionMessage = "Change not found or not in a pending state.";
                actionSuccess = false;
            }
            else
            {
                actionMessage = $"Change #{changeId} rejected.";
                actionSuccess = true;
            }

            await LoadSuggestion();
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Failed to reject change {ChangeId}", changeId);
            actionMessage = "An error occurred while rejecting the change.";
            actionSuccess = false;
        }
        finally
        {
            isProcessing = false;
        }
    }

    private async Task ResolveConflict(int changeId)
    {
        if (string.IsNullOrWhiteSpace(conflictResolution))
        {
            actionMessage = "Please provide a resolution note.";
            actionSuccess = false;
            return;
        }

        isProcessing = true;
        actionMessage = null;

        try
        {
            var userId = await GetAdminUserId();
            var result = await ReviewService.ResolveConflictAsync(SuggestionId, changeId, userId, conflictResolution);
            if (result == null)
            {
                actionMessage = "Change not found or not in a conflicted state.";
                actionSuccess = false;
            }
            else if (result.Status == EditSuggestionChangeStatus.Applied)
            {
                actionMessage = $"Conflict resolved. Change #{changeId} applied.";
                actionSuccess = true;
                conflictResolution = string.Empty;
            }
            else
            {
                actionMessage = $"Change #{changeId} still conflicted: {result.ConflictReason}";
                actionSuccess = false;
            }

            await LoadSuggestion();
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Failed to resolve conflict for change {ChangeId}", changeId);
            actionMessage = "An error occurred while resolving the conflict.";
            actionSuccess = false;
        }
        finally
        {
            isProcessing = false;
        }
    }

    private async Task AttributeDiscId(int changeId)
    {
        if (!selectedDestination.TryGetValue(changeId, out var destinationReleaseDiscId) || destinationReleaseDiscId <= 0)
        {
            actionMessage = "Please choose a release-disc to attribute the Disc ID to.";
            actionSuccess = false;
            return;
        }

        isProcessing = true;
        actionMessage = null;

        try
        {
            var userId = await GetAdminUserId();
            var result = await ReviewService.AttributeDiscIdAsync(SuggestionId, changeId, destinationReleaseDiscId, userId);
            if (result == null)
            {
                actionMessage = "Change not found or not resolvable.";
                actionSuccess = false;
            }
            else if (result.Status == EditSuggestionChangeStatus.Applied)
            {
                actionMessage = $"Disc ID attributed. Change #{changeId} applied.";
                actionSuccess = true;
            }
            else
            {
                actionMessage = $"Change #{changeId} still conflicted: {result.ConflictReason}";
                actionSuccess = false;
            }

            await LoadSuggestion();
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Failed to attribute Disc ID for change {ChangeId}", changeId);
            actionMessage = "An error occurred while attributing the Disc ID.";
            actionSuccess = false;
        }
        finally
        {
            isProcessing = false;
        }
    }

    private async Task SwapDiscId(int changeId)
    {
        if (!selectedDestination.TryGetValue(changeId, out var secondaryReleaseDiscId) || secondaryReleaseDiscId <= 0)
        {
            actionMessage = "Please choose the sibling release-disc that should receive the displaced Disc ID.";
            actionSuccess = false;
            return;
        }

        isProcessing = true;
        actionMessage = null;

        try
        {
            var userId = await GetAdminUserId();
            var result = await ReviewService.SwapDiscIdAsync(SuggestionId, changeId, secondaryReleaseDiscId, userId);
            if (result == null)
            {
                actionMessage = "Change not found or not resolvable.";
                actionSuccess = false;
            }
            else if (result.Status == EditSuggestionChangeStatus.Applied)
            {
                actionMessage = $"Disc IDs swapped. Change #{changeId} applied.";
                actionSuccess = true;
            }
            else
            {
                actionMessage = $"Change #{changeId} still conflicted: {result.ConflictReason}";
                actionSuccess = false;
            }

            await LoadSuggestion();
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Failed to swap Disc ID for change {ChangeId}", changeId);
            actionMessage = "An error occurred while swapping the Disc ID.";
            actionSuccess = false;
        }
        finally
        {
            isProcessing = false;
        }
    }

    private async Task RejectConflictedChange(int changeId)
    {
        isProcessing = true;
        actionMessage = null;

        try
        {
            var userId = await GetAdminUserId();
            var reason = rejectionReasons.GetValueOrDefault(changeId);
            var result = await ReviewService.RejectChangeAsync(SuggestionId, changeId, userId, reason);
            actionMessage = result == null ? "Change not found." : $"Change #{changeId} rejected.";
            actionSuccess = result != null;
            await LoadSuggestion();
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Failed to reject conflicted change {ChangeId}", changeId);
            actionMessage = "An error occurred while rejecting the change.";
            actionSuccess = false;
        }
        finally
        {
            isProcessing = false;
        }
    }

    private async Task ApproveAll()
    {
        isProcessing = true;
        actionMessage = null;

        try
        {
            var userId = await GetAdminUserId();
            var result = await ReviewService.ApproveAllPendingAsync(SuggestionId, userId);
            if (result == null)
            {
                actionMessage = "Suggestion not found.";
                actionSuccess = false;
            }
            else
            {
                actionMessage = $"All pending changes processed. Bundle status: {result.Status}";
                actionSuccess = result.Status is EditSuggestionStatus.Approved or EditSuggestionStatus.PartiallyApproved;
            }

            await LoadSuggestion();
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Failed to approve all changes for suggestion {SuggestionId}", SuggestionId);
            actionMessage = "An error occurred while approving changes.";
            actionSuccess = false;
        }
        finally
        {
            isProcessing = false;
        }
    }

    private async Task<string> GetAdminUserId()
    {
        var authState = await AuthStateProvider.GetAuthenticationStateAsync();
        return UserManager.GetUserId(authState.User)
            ?? throw new InvalidOperationException("Admin user ID could not be resolved.");
    }

    private bool HasRejectionReason(int changeId) =>
        rejectionReasons.TryGetValue(changeId, out var reason) && !string.IsNullOrWhiteSpace(reason);

    private void SetDestination(int changeId, Microsoft.AspNetCore.Components.ChangeEventArgs e)
    {
        if (int.TryParse(e.Value?.ToString(), out var id))
        {
            selectedDestination[changeId] = id;
        }
    }

    private static string CandidateLabel(DiscIdConflictCandidate c)
    {
        var name = c.MediaTitle ?? c.MediaItemSlug ?? "(unknown)";
        var disc = string.IsNullOrEmpty(c.DiscSlug) ? $"disc {c.DiscIndex}" : c.DiscSlug;
        var has = string.IsNullOrEmpty(c.CurrentGlobalDiscId) ? "no id" : $"has {c.CurrentGlobalDiscId}";
        return $"{name} / {c.ReleaseSlug} / {disc} — {has}";
    }

    private bool CanReview => suggestion is not null && suggestion.Status.IsReviewable();

    private static string FormatJson(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            return JsonSerializer.Serialize(doc, IndentedJson);
        }
        catch
        {
            return json;
        }
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

    private static string GetChangeStatusBadge(EditSuggestionChangeStatus status) => status switch
    {
        EditSuggestionChangeStatus.Pending => "bg-warning text-dark",
        EditSuggestionChangeStatus.Approved => "bg-info",
        EditSuggestionChangeStatus.Applied => "bg-success",
        EditSuggestionChangeStatus.Rejected => "bg-danger",
        EditSuggestionChangeStatus.Conflicted => "bg-danger",
        _ => "bg-light text-dark",
    };

    private static (string Text, string Url) GetRootAdminLink() => ("Admin", "/admin");
    private static (string Text, string Url) GetChangesLink() => ("Edit Suggestions", "/admin/changes");
}
