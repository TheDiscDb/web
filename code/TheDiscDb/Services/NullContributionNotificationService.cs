using TheDiscDb.Web.Data;

namespace TheDiscDb.Services;

/// <summary>
/// No-op implementation used when Mailgun is not configured.
/// </summary>
public class NullContributionNotificationService : IContributionNotificationService
{
    private readonly ILogger<NullContributionNotificationService> logger;

    public NullContributionNotificationService(ILogger<NullContributionNotificationService> logger)
    {
        this.logger = logger;
    }

    public Task NotifyContributionCreatedAsync(UserContribution contribution, string? userEmail, string? userName)
    {
        logger.LogDebug("Email notifications disabled (Mailgun not configured). Skipping notification for contribution {Id}", contribution.Id);
        return Task.CompletedTask;
    }

    public Task NotifyContributionImportedAsync(UserContribution contribution, string? userEmail)
    {
        logger.LogDebug("Email notifications disabled (Mailgun not configured). Skipping imported notification for contribution {Id}", contribution.Id);
        return Task.CompletedTask;
    }
}
