using TheDiscDb.Web.Data;

namespace TheDiscDb.Services;

public interface IContributionNotificationService
{
    Task NotifyContributionCreatedAsync(UserContribution contribution, string? userEmail, string? userName);
    Task NotifyContributionImportedAsync(UserContribution contribution, string? userEmail);
    Task NotifyMessageFromUserAsync(UserContribution contribution, string message, string? userName, string? userEmail);
    Task NotifyMessageFromAdminAsync(UserContribution contribution, string message, string? userEmail);
}
