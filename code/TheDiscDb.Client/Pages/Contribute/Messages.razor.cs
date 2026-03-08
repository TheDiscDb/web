using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Components;
using StrawberryShake;
using TheDiscDb.Client.Contributions;

namespace TheDiscDb.Client.Pages.Contribute;

[Authorize]
public partial class Messages : ComponentBase
{
    [Inject]
    private IContributionClient ContributionClient { get; set; } = null!;

    private (string Text, string Url)[] breadcrumbItems = [];
    private List<IGetMessageThreads_MessageThreads>? threads;
    private bool isLoading = true;
    private string? loadError;

    protected override async Task OnInitializedAsync()
    {
        breadcrumbItems = [
            BreadCrumbHelper.GetRootContributionLink()
        ];

        try
        {
            var result = await ContributionClient.GetMessageThreads.ExecuteAsync();
            if (result.IsSuccessResult() && result.Data != null)
            {
                threads = result.Data.MessageThreads.ToList();
            }
            else
            {
                loadError = "Failed to load messages.";
            }
        }
        catch (Exception ex)
        {
            loadError = "Failed to load messages. Please try again.";
        }
        finally
        {
            isLoading = false;
        }
    }

    private static string FormatTime(DateTimeOffset time)
    {
        var diff = DateTimeOffset.UtcNow - time;
        if (diff.TotalMinutes < 1) return "just now";
        if (diff.TotalHours < 1) return $"{(int)diff.TotalMinutes}m ago";
        if (diff.TotalDays < 1) return $"{(int)diff.TotalHours}h ago";
        if (diff.TotalDays < 7) return $"{(int)diff.TotalDays}d ago";
        return time.ToString("MMM d, yyyy");
    }
}
