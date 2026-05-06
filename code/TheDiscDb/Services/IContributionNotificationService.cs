using TheDiscDb.Web.Data;

namespace TheDiscDb.Services;

public interface IContributionNotificationService
{
    Task NotifyContributionCreatedAsync(UserContribution contribution, string? userEmail, string? userName, CancellationToken cancellationToken = default);
    Task NotifyContributionImportedAsync(UserContribution contribution, string? userEmail, CancellationToken cancellationToken = default);
    Task NotifyMessageFromUserAsync(UserContribution contribution, string message, string? userName, string? userEmail, CancellationToken cancellationToken = default);
    Task NotifyMessageFromAdminAsync(UserContribution contribution, string message, string? userEmail, CancellationToken cancellationToken = default);
}
