using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Components;
using StrawberryShake;
using TheDiscDb.Client.Contributions;

namespace TheDiscDb.Client.Pages.Contribute;

[Authorize]
public partial class AddExistingDiscs : CancellableComponentBase
{
    [Parameter]
    public string BoxsetId { get; set; } = string.Empty;

    [Inject]
    IContributionClient ContributionClient { get; set; } = null!;

    [Inject]
    ISearchMediaItemsForBoxsetQuery SearchQuery { get; set; } = null!;

    [Inject]
    NavigationManager Navigation { get; set; } = null!;

    private string searchTerm = string.Empty;
    private bool isSearching;
    private IReadOnlyList<ISearchMediaItemsForBoxset_MediaItems_Nodes>? searchResults;
    private HashSet<string> addedDiscPaths = new();
    private HashSet<string> addingDiscPaths = new();
    private string? boxsetSlug;
    private string? errorMessage;
    private string? successMessage;
    private System.Timers.Timer? debounceTimer;

    protected override async Task OnInitializedAsync()
    {
        await LoadExistingBoxsetMembers();

        // Eagerly load every release that matches the boxset's slug, regardless of search term.
        // The user can then refine by typing into the title input.
        if (!string.IsNullOrEmpty(boxsetSlug))
        {
            await ExecuteSearch();
        }
    }

    private async Task LoadExistingBoxsetMembers()
    {
        try
        {
            var filter = new UserContributionBoxsetFilterInput
            {
                EncodedId = new EncodedIdOperationFilterInput { Eq = BoxsetId }
            };

            var result = await ContributionClient.GetBoxsetDetail.ExecuteAsync(filter);
            if (result != null && result.IsSuccessResult())
            {
                var boxset = result.Data?.MyBoxsets?.Nodes?.FirstOrDefault();
                if (boxset != null)
                {
                    boxsetSlug = boxset.Slug;
                    if (boxset.Members != null)
                    {
                        foreach (var member in boxset.Members)
                        {
                            if (!string.IsNullOrEmpty(member.ExistingDiscPath))
                            {
                                addedDiscPaths.Add(member.ExistingDiscPath);
                            }
                        }
                    }
                }
            }
        }
        catch (Exception)
        {
            // Non-fatal
        }
    }

    private async Task OnSearchChanged()
    {
        debounceTimer?.Stop();
        debounceTimer?.Dispose();

        // Don't bail on empty input — re-search with empty title to show ALL releases that match
        // the boxset slug. Users can clear the box to reset to the full list.
        debounceTimer = new System.Timers.Timer(300);
        debounceTimer.Elapsed += async (_, _) =>
        {
            debounceTimer?.Stop();
            await InvokeAsync(async () =>
            {
                await ExecuteSearch();
                StateHasChanged();
            });
        };
        debounceTimer.AutoReset = false;
        debounceTimer.Start();
    }

    private async Task ExecuteSearch()
    {
        if (string.IsNullOrEmpty(boxsetSlug))
        {
            // No slug yet — can't filter to matching releases. Avoid running a query that would
            // pull every media item in the database.
            return;
        }

        isSearching = true;
        errorMessage = null;

        try
        {
            // Server-side filter: titles containing searchTerm AND having at least one release
            // whose slug matches the boxset slug. Empty searchTerm means all titles. The
            // releases() projection itself is also filtered to the matching slug so the client
            // doesn't have to drop sibling releases.
            var result = await SearchQuery.ExecuteAsync(searchTerm ?? string.Empty, boxsetSlug);
            if (result != null && result.IsSuccessResult())
            {
                searchResults = result.Data?.MediaItems?.Nodes;
            }
            else
            {
                searchResults = null;
                errorMessage = "Search failed. Please try again.";
            }
        }
        catch (Exception)
        {
            searchResults = null;
            errorMessage = "Search failed. Please try again.";
        }
        finally
        {
            isSearching = false;
        }
    }

    private static string BuildDiscPath(string? mediaType, string? tmdbId, string? releaseSlug, string? discSlug)
    {
        if (string.IsNullOrEmpty(mediaType) || string.IsNullOrEmpty(tmdbId) ||
            string.IsNullOrEmpty(releaseSlug) || string.IsNullOrEmpty(discSlug))
        {
            return string.Empty;
        }

        return $"{mediaType}/{tmdbId}/{releaseSlug}/{discSlug}";
    }

    private async Task AddExistingDisc(string discPath, string discName, string discFormat)
    {
        if (string.IsNullOrEmpty(discPath) || addingDiscPaths.Contains(discPath)) return;

        addingDiscPaths.Add(discPath);
        errorMessage = null;
        successMessage = null;

        try
        {
            var result = await ContributionClient.AddExistingDiscToBoxset.ExecuteAsync(
                new AddExistingDiscToBoxsetInput
                {
                    BoxsetId = BoxsetId,
                    ExistingDiscPath = discPath,
                    DiscName = discName,
                    DiscFormat = discFormat
                });

            if (result == null || !result.IsSuccessResult())
            {
                errorMessage = "Failed to add disc — server error. Please try again.";
                return;
            }

            if (result.Data?.AddExistingDiscToBoxset?.Errors is { Count: > 0 } errors)
            {
                var error = errors[0];
                errorMessage = error switch
                {
                    IAddExistingDiscToBoxset_AddExistingDiscToBoxset_Errors_ExistingDiscAlreadyInBoxsetError e => e.Message,
                    IAddExistingDiscToBoxset_AddExistingDiscToBoxset_Errors_BoxsetNotFoundError e => e.Message,
                    IAddExistingDiscToBoxset_AddExistingDiscToBoxset_Errors_InvalidDiscPathError e => e.Message,
                    IAddExistingDiscToBoxset_AddExistingDiscToBoxset_Errors_InvalidBoxsetStatusError e => e.Message,
                    IAddExistingDiscToBoxset_AddExistingDiscToBoxset_Errors_AuthenticationError e => e.Message,
                    IAddExistingDiscToBoxset_AddExistingDiscToBoxset_Errors_MismatchedReleaseSlugError e => e.Message,
                    _ => $"An unexpected error occurred ({error.Code})"
                };
                return;
            }

            addedDiscPaths.Add(discPath);
            successMessage = $"Added \"{discName}\" to boxset.";
        }
        catch (Exception)
        {
            errorMessage = "Failed to add disc. Please try again.";
        }
        finally
        {
            addingDiscPaths.Remove(discPath);
        }
    }

    public override void Dispose()
    {
        debounceTimer?.Stop();
        debounceTimer?.Dispose();
        base.Dispose();
    }
}
