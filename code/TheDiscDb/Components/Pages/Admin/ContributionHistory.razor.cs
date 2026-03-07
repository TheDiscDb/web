using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using TheDiscDb.Services;
using TheDiscDb.Services.Server;
using TheDiscDb.Web.Data;

namespace TheDiscDb.Components.Pages.Admin;

[Authorize(Roles = DefaultRoles.Administrator)]
public partial class ContributionHistory : ComponentBase, IAsyncDisposable
{
    [Inject]
    private IDbContextFactory<SqlServerDataContext> DbFactory { get; set; } = null!;

    [Inject]
    private UserManager<TheDiscDbUser> UserManager { get; set; } = null!;

    [Inject]
    private IPrincipalProvider PrincipalProvider { get; set; } = null!;

    [Parameter]
    public string? ContributionId { get; set; }

    private SqlServerDataContext database = default!;
    private UserContribution? Contribution { get; set; }
    private List<TimelineEntry>? TimelineItems { get; set; }
    private Dictionary<string, string> userNameCache = new();
    private string newMessage = string.Empty;
    private string? sendError;

    public record TimelineEntry(string TypeLabel, string BadgeClass, DateTimeOffset Timestamp, string UserId, string Description);

    protected override async Task OnInitializedAsync()
    {
        if (DbFactory != null)
        {
            this.database = await DbFactory.CreateDbContextAsync();

            this.Contribution = await database.UserContributions
                .FirstOrDefaultAsync(uc => uc.Id.ToString() == ContributionId);

            if (this.Contribution != null)
            {
                await LoadHistory();
            }
        }
    }

    private async Task LoadHistory()
    {
        var historyItems = await database.ContributionHistory
            .Where(h => h.ContributionId == Contribution!.Id)
            .OrderByDescending(h => h.TimeStamp)
            .ToListAsync();

        var messages = await database.UserMessages
            .Where(m => m.ContributionId == Contribution!.Id)
            .OrderByDescending(m => m.CreatedAt)
            .ToListAsync();

        // Merge into unified timeline
        var timeline = new List<TimelineEntry>();

        foreach (var h in historyItems)
        {
            timeline.Add(new TimelineEntry(
                h.Type.ToString(),
                GetHistoryBadgeClass(h.Type),
                h.TimeStamp,
                h.UserId,
                h.Description));
        }

        foreach (var m in messages)
        {
            timeline.Add(new TimelineEntry(
                m.Type == UserMessageType.AdminMessage ? "AdminMessage" : "UserMessage",
                m.Type == UserMessageType.AdminMessage ? "bg-primary" : "bg-secondary",
                m.CreatedAt,
                m.FromUserId,
                m.Message));
        }

        TimelineItems = timeline.OrderByDescending(t => t.Timestamp).ToList();

        // Cache user names for display
        var userIds = TimelineItems.Select(t => t.UserId).Distinct().ToList();
        foreach (var userId in userIds)
        {
            if (!userNameCache.ContainsKey(userId))
            {
                var user = await UserManager.FindByIdAsync(userId);
                userNameCache[userId] = user?.UserName ?? userId;
            }
        }
    }

    private string GetUserName(string userId) =>
        userNameCache.TryGetValue(userId, out var name) ? name : userId;

    private static string GetHistoryBadgeClass(ContributionHistoryType type) => type switch
    {
        ContributionHistoryType.Created => "bg-success",
        ContributionHistoryType.StatusChanged => "bg-info",
        ContributionHistoryType.Deleted => "bg-danger",
        ContributionHistoryType.AdminMessage => "bg-primary",
        ContributionHistoryType.UserMessage => "bg-secondary",
        _ => "bg-light text-dark"
    };

    private async Task SendMessage()
    {
        if (this.Contribution == null || string.IsNullOrWhiteSpace(newMessage))
            return;

        try
        {
            sendError = null;
            var adminUser = PrincipalProvider.Principal;
            var adminUserId = adminUser?.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
            if (string.IsNullOrEmpty(adminUserId))
            {
                sendError = "Unable to determine current admin user.";
                return;
            }

            var userMessage = new UserMessage
            {
                ContributionId = this.Contribution.Id,
                FromUserId = adminUserId,
                ToUserId = this.Contribution.UserId,
                Message = newMessage,
                IsRead = false,
                CreatedAt = DateTimeOffset.UtcNow,
                Type = UserMessageType.AdminMessage
            };

            database.UserMessages.Add(userMessage);
            await database.SaveChangesAsync();

            newMessage = string.Empty;
            await LoadHistory();
        }
        catch (Exception ex)
        {
            sendError = $"Failed to send message: {ex.Message}";
        }
    }

    public async ValueTask DisposeAsync() => await database.DisposeAsync();
}
