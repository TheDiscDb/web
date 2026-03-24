using System.Net;
using Microsoft.Extensions.Options;
using TheDiscDb.Web.Data;
using TheDiscDb.Web.Email;

namespace TheDiscDb.Services;

public class ContributionNotificationService : IContributionNotificationService
{
    private readonly MailgunClient mailgun;
    private readonly IOptionsMonitor<MailgunOptions> options;
    private readonly ILogger<ContributionNotificationService> logger;

    public ContributionNotificationService(
        MailgunClient mailgun,
        IOptionsMonitor<MailgunOptions> options,
        ILogger<ContributionNotificationService> logger)
    {
        this.mailgun = mailgun;
        this.options = options;
        this.logger = logger;
    }

    public async Task NotifyContributionCreatedAsync(UserContribution contribution, string? userEmail, string? userName)
    {
        var opts = options.CurrentValue;
        var displayName = userName ?? "A user";
        var title = $"{contribution.Title} ({contribution.Year})";

        await SendAdminNotificationAsync(contribution, opts, displayName, userEmail, title);
        await SendUserConfirmationAsync(contribution, opts, userEmail, title);
    }

    private async Task SendAdminNotificationAsync(
        UserContribution contribution,
        MailgunOptions opts,
        string displayName,
        string? userEmail,
        string title)
    {
        if (string.IsNullOrEmpty(opts.AdminEmail))
        {
            logger.LogWarning("Mailgun AdminEmail is not configured — skipping admin notification for contribution {Id}", contribution.Id);
            return;
        }

        var adminUrl = $"https://thediscdb.com/admin/contribution/{contribution.Id}";

        var eName = E(displayName);
        var eEmail = E(userEmail);
        var eTitle = E(contribution.Title);
        var eYear = E(contribution.Year);
        var eReleaseTitle = E(contribution.ReleaseTitle);
        var eMediaType = E(contribution.MediaType);
        var eAsin = E(contribution.Asin);
        var eUpc = E(contribution.Upc);

        var html = $"""
            <div style="font-family: -apple-system, BlinkMacSystemFont, 'Segoe UI', Roboto, sans-serif; max-width: 600px; margin: 0 auto;">
                <h2 style="color: #00213F;">New Contribution Submitted</h2>
                <p><strong>{eName}</strong>{(string.IsNullOrEmpty(userEmail) ? "" : $" ({eEmail})")} submitted a new contribution.</p>
                <table style="width: 100%; border-collapse: collapse; margin: 16px 0;">
                    <tr><td style="padding: 8px; border-bottom: 1px solid #eee; color: #666;">Title</td><td style="padding: 8px; border-bottom: 1px solid #eee;"><strong>{eTitle}</strong></td></tr>
                    <tr><td style="padding: 8px; border-bottom: 1px solid #eee; color: #666;">Year</td><td style="padding: 8px; border-bottom: 1px solid #eee;">{eYear}</td></tr>
                    <tr><td style="padding: 8px; border-bottom: 1px solid #eee; color: #666;">Release Title</td><td style="padding: 8px; border-bottom: 1px solid #eee;">{eReleaseTitle}</td></tr>
                    <tr><td style="padding: 8px; border-bottom: 1px solid #eee; color: #666;">Media Type</td><td style="padding: 8px; border-bottom: 1px solid #eee;">{eMediaType}</td></tr>
                    {(string.IsNullOrEmpty(contribution.Asin) ? "" : $"<tr><td style=\"padding: 8px; border-bottom: 1px solid #eee; color: #666;\">ASIN</td><td style=\"padding: 8px; border-bottom: 1px solid #eee;\">{eAsin}</td></tr>")}
                    {(string.IsNullOrEmpty(contribution.Upc) ? "" : $"<tr><td style=\"padding: 8px; border-bottom: 1px solid #eee; color: #666;\">UPC</td><td style=\"padding: 8px; border-bottom: 1px solid #eee;\">{eUpc}</td></tr>")}
                </table>
                <p><a href="{adminUrl}" style="display: inline-block; padding: 10px 20px; background-color: #00213F; color: #ffffff; text-decoration: none; border-radius: 4px;">Review Contribution</a></p>
            </div>
            """;

        var message = new MailgunMessage
        {
            To = [opts.AdminEmail],
            Subject = $"New Contribution: {title} — {contribution.ReleaseTitle}",
            Html = html,
            Tags = ["contribution-admin-notification"]
        };

        try
        {
            await mailgun.SendAsync(message);
            logger.LogInformation("Sent admin notification for contribution {Id} to {Email}", contribution.Id, opts.AdminEmail);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to send admin notification for contribution {Id}", contribution.Id);
        }
    }

    private async Task SendUserConfirmationAsync(
        UserContribution contribution,
        MailgunOptions opts,
        string? userEmail,
        string title)
    {
        if (string.IsNullOrEmpty(userEmail))
        {
            logger.LogDebug("No user email available — skipping user confirmation for contribution {Id}", contribution.Id);
            return;
        }

        var eTitle = E(contribution.Title);
        var eYear = E(contribution.Year);
        var eReleaseTitle = E(contribution.ReleaseTitle);
        var eMediaType = E(contribution.MediaType);

        var html = $"""
            <div style="font-family: -apple-system, BlinkMacSystemFont, 'Segoe UI', Roboto, sans-serif; max-width: 600px; margin: 0 auto;">
                <h2 style="color: #00213F;">Thanks for Your Contribution!</h2>
                <p>We've received your submission and an admin will review it shortly.</p>
                <table style="width: 100%; border-collapse: collapse; margin: 16px 0;">
                    <tr><td style="padding: 8px; border-bottom: 1px solid #eee; color: #666;">Title</td><td style="padding: 8px; border-bottom: 1px solid #eee;"><strong>{eTitle}</strong></td></tr>
                    <tr><td style="padding: 8px; border-bottom: 1px solid #eee; color: #666;">Year</td><td style="padding: 8px; border-bottom: 1px solid #eee;">{eYear}</td></tr>
                    <tr><td style="padding: 8px; border-bottom: 1px solid #eee; color: #666;">Release Title</td><td style="padding: 8px; border-bottom: 1px solid #eee;">{eReleaseTitle}</td></tr>
                    <tr><td style="padding: 8px; border-bottom: 1px solid #eee; color: #666;">Media Type</td><td style="padding: 8px; border-bottom: 1px solid #eee;">{eMediaType}</td></tr>
                </table>
                <p style="color: #666; font-size: 14px;">You'll receive updates about your contribution's status through the Messages feature on TheDiscDb.</p>
                <p style="color: #999; font-size: 12px;">If you didn't submit this contribution, please ignore this email.</p>
            </div>
            """;

        var message = new MailgunMessage
        {
            To = [userEmail],
            Subject = $"Your contribution has been submitted — {title}",
            Html = html,
            Tags = ["contribution-user-confirmation"]
        };

        try
        {
            await mailgun.SendAsync(message);
            logger.LogInformation("Sent user confirmation for contribution {Id} to {Email}", contribution.Id, userEmail);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to send user confirmation for contribution {Id}", contribution.Id);
        }
    }

    private static string E(string? value) => WebUtility.HtmlEncode(value ?? string.Empty);
}
