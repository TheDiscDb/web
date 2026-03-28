using System.Net;
using Microsoft.Extensions.Options;
using TheDiscDb.Services.Server;
using TheDiscDb.Web.Data;
using TheDiscDb.Web.Email;

namespace TheDiscDb.Services;

public class ContributionNotificationService : IContributionNotificationService
{
    private readonly MailgunClient mailgun;
    private readonly IOptionsMonitor<MailgunOptions> options;
    private readonly IdEncoder idEncoder;
    private readonly ILogger<ContributionNotificationService> logger;

    public ContributionNotificationService(
        MailgunClient mailgun,
        IOptionsMonitor<MailgunOptions> options,
        IdEncoder idEncoder,
        ILogger<ContributionNotificationService> logger)
    {
        this.mailgun = mailgun;
        this.options = options;
        this.idEncoder = idEncoder;
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

    public async Task NotifyContributionImportedAsync(UserContribution contribution, string? userEmail)
    {
        if (string.IsNullOrEmpty(userEmail))
        {
            logger.LogDebug("No user email available — skipping imported notification for contribution {Id}", contribution.Id);
            return;
        }

        var title = $"{contribution.Title} ({contribution.Year})";
        var mediaType = contribution.MediaType?.ToLowerInvariant() ?? "movie";
        var hasItemLink = !string.IsNullOrEmpty(contribution.TitleSlug);
        var itemUrl = hasItemLink ? $"https://thediscdb.com/{mediaType}/{contribution.TitleSlug}" : "https://thediscdb.com";

        var eTitle = E(contribution.Title);
        var eYear = E(contribution.Year);
        var eReleaseTitle = E(contribution.ReleaseTitle);
        var eMediaType = E(contribution.MediaType);

        var linkHtml = hasItemLink
            ? $"""<p><a href="{itemUrl}" style="display: inline-block; padding: 10px 20px; background-color: #00213F; color: #ffffff; text-decoration: none; border-radius: 4px;">View on TheDiscDb</a></p>"""
            : $"""<p><a href="{itemUrl}" style="display: inline-block; padding: 10px 20px; background-color: #00213F; color: #ffffff; text-decoration: none; border-radius: 4px;">Visit TheDiscDb</a></p>""";

        var html = $"""
            <div style="font-family: -apple-system, BlinkMacSystemFont, 'Segoe UI', Roboto, sans-serif; max-width: 600px; margin: 0 auto;">
                <h2 style="color: #00213F;">Your Contribution Has Been Imported!</h2>
                <p>Great news — your contribution has been reviewed, approved, and imported into TheDiscDb.</p>
                <table style="width: 100%; border-collapse: collapse; margin: 16px 0;">
                    <tr><td style="padding: 8px; border-bottom: 1px solid #eee; color: #666;">Title</td><td style="padding: 8px; border-bottom: 1px solid #eee;"><strong>{eTitle}</strong></td></tr>
                    <tr><td style="padding: 8px; border-bottom: 1px solid #eee; color: #666;">Year</td><td style="padding: 8px; border-bottom: 1px solid #eee;">{eYear}</td></tr>
                    <tr><td style="padding: 8px; border-bottom: 1px solid #eee; color: #666;">Release</td><td style="padding: 8px; border-bottom: 1px solid #eee;">{eReleaseTitle}</td></tr>
                    <tr><td style="padding: 8px; border-bottom: 1px solid #eee; color: #666;">Type</td><td style="padding: 8px; border-bottom: 1px solid #eee;">{eMediaType}</td></tr>
                </table>
                {linkHtml}
                <p style="color: #666; font-size: 14px;">Thank you for contributing to TheDiscDb! Your submission helps build the most complete disc database on the web.</p>
            </div>
            """;

        var message = new MailgunMessage
        {
            To = [userEmail],
            Subject = $"Your contribution has been imported — {title}",
            Html = html,
            Tags = ["contribution-imported"]
        };

        try
        {
            await mailgun.SendAsync(message);
            logger.LogInformation("Sent imported notification for contribution {Id} to {Email}", contribution.Id, userEmail);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to send imported notification for contribution {Id}", contribution.Id);
        }
    }

    public async Task NotifyMessageFromUserAsync(UserContribution contribution, string message, string? userName, string? userEmail)
    {
        var opts = options.CurrentValue;
        if (string.IsNullOrEmpty(opts.AdminEmail))
        {
            logger.LogWarning("Mailgun AdminEmail is not configured — skipping message notification for contribution {Id}", contribution.Id);
            return;
        }

        var title = $"{contribution.Title} ({contribution.Year})";
        var adminUrl = $"https://thediscdb.com/admin/contribution/{contribution.Id}/history";
        var eName = E(userName ?? "A contributor");
        var eEmail = E(userEmail);
        var eTitle = E(title);
        var eRelease = E(contribution.ReleaseTitle);
        var eMessage = E(message);

        var html = $"""
            <div style="font-family: -apple-system, BlinkMacSystemFont, 'Segoe UI', Roboto, sans-serif; max-width: 600px; margin: 0 auto;">
                <h2 style="color: #00213F;">New Message on Contribution</h2>
                <p><strong>{eName}</strong>{(string.IsNullOrEmpty(userEmail) ? "" : $" ({eEmail})")} sent a message on <strong>{eTitle}</strong> — {eRelease}:</p>
                <blockquote style="margin: 16px 0; padding: 12px 16px; background: #f5f5f5; border-left: 4px solid #00213F; white-space: pre-wrap;">{eMessage}</blockquote>
                <p><a href="{adminUrl}" style="display: inline-block; padding: 10px 20px; background-color: #00213F; color: #ffffff; text-decoration: none; border-radius: 4px;">View Contribution</a></p>
            </div>
            """;

        var email = new MailgunMessage
        {
            To = [opts.AdminEmail],
            Subject = $"Message from {userName ?? "contributor"}: {title} — {contribution.ReleaseTitle}",
            Html = html,
            Tags = ["message-admin-notification"]
        };

        try
        {
            await mailgun.SendAsync(email);
            logger.LogInformation("Sent message notification to admin for contribution {Id}", contribution.Id);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to send message notification to admin for contribution {Id}", contribution.Id);
        }
    }

    public async Task NotifyMessageFromAdminAsync(UserContribution contribution, string message, string? userEmail)
    {
        if (string.IsNullOrEmpty(userEmail))
        {
            logger.LogDebug("No user email available — skipping admin message notification for contribution {Id}", contribution.Id);
            return;
        }

        var title = $"{contribution.Title} ({contribution.Year})";
        var encodedId = idEncoder.Encode(contribution.Id);
        var messagesUrl = $"https://thediscdb.com/contribution/{encodedId}/messages";
        var eTitle = E(title);
        var eRelease = E(contribution.ReleaseTitle);
        var eMessage = E(message);

        var html = $"""
            <div style="font-family: -apple-system, BlinkMacSystemFont, 'Segoe UI', Roboto, sans-serif; max-width: 600px; margin: 0 auto;">
                <h2 style="color: #00213F;">New Message from TheDiscDb</h2>
                <p>An admin sent you a message about your contribution <strong>{eTitle}</strong> — {eRelease}:</p>
                <blockquote style="margin: 16px 0; padding: 12px 16px; background: #f5f5f5; border-left: 4px solid #00213F; white-space: pre-wrap;">{eMessage}</blockquote>
                <p><a href="{messagesUrl}" style="display: inline-block; padding: 10px 20px; background-color: #00213F; color: #ffffff; text-decoration: none; border-radius: 4px;">View Messages</a></p>
            </div>
            """;

        var email = new MailgunMessage
        {
            To = [userEmail],
            Subject = $"Message about your contribution: {title}",
            Html = html,
            Tags = ["message-user-notification"]
        };

        try
        {
            await mailgun.SendAsync(email);
            logger.LogInformation("Sent admin message notification for contribution {Id} to {Email}", contribution.Id, userEmail);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to send admin message notification for contribution {Id}", contribution.Id);
        }
    }

    private static string E(string? value) => WebUtility.HtmlEncode(value ?? string.Empty);
}
