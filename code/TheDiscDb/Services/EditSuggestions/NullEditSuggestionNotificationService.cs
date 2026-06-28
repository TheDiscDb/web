namespace TheDiscDb.Services.EditSuggestions;

using System.Threading;
using System.Threading.Tasks;
using TheDiscDb.Web.Data;

/// <summary>
/// No-op implementation used when edit-suggestion notifications are disabled
/// (the default) or when Mailgun is not configured.
/// </summary>
public sealed class NullEditSuggestionNotificationService : IEditSuggestionNotificationService
{
    private readonly ILogger<NullEditSuggestionNotificationService> logger;

    public NullEditSuggestionNotificationService(ILogger<NullEditSuggestionNotificationService> logger)
    {
        this.logger = logger;
    }

    public Task NotifySuggestionSubmittedAsync(EditSuggestion suggestion, string? userEmail, string? userName, CancellationToken cancellationToken = default)
    {
        logger.LogDebug("Edit-suggestion notifications disabled. Skipping submitted notification for suggestion {Id}", suggestion.Id);
        return Task.CompletedTask;
    }

    public Task NotifySuggestionResolvedAsync(EditSuggestion suggestion, string? userEmail, CancellationToken cancellationToken = default)
    {
        logger.LogDebug("Edit-suggestion notifications disabled. Skipping resolved notification for suggestion {Id}", suggestion.Id);
        return Task.CompletedTask;
    }

    public Task NotifyMessageFromUserAsync(EditSuggestion suggestion, string message, string? userName, string? userEmail, CancellationToken cancellationToken = default)
    {
        logger.LogDebug("Edit-suggestion notifications disabled. Skipping user-message notification for suggestion {Id}", suggestion.Id);
        return Task.CompletedTask;
    }

    public Task NotifyMessageFromAdminAsync(EditSuggestion suggestion, string message, string? userEmail, CancellationToken cancellationToken = default)
    {
        logger.LogDebug("Edit-suggestion notifications disabled. Skipping admin-message notification for suggestion {Id}", suggestion.Id);
        return Task.CompletedTask;
    }
}
