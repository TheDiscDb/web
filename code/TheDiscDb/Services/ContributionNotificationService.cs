using System.Net;
using Markdig;
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

        var html = WrapInEmailLayout($"""
                <h2 style="color: {BrandColor}; margin-top: 0;">New Contribution Submitted</h2>
                <p><strong>{eName}</strong>{(string.IsNullOrEmpty(userEmail) ? "" : $" ({eEmail})")} submitted a new contribution.</p>
                <table style="width: 100%; border-collapse: collapse; margin: 16px 0;">
                    <tr><td style="padding: 8px; border-bottom: 1px solid #eee; color: #666;">Title</td><td style="padding: 8px; border-bottom: 1px solid #eee;"><strong>{eTitle}</strong></td></tr>
                    <tr><td style="padding: 8px; border-bottom: 1px solid #eee; color: #666;">Year</td><td style="padding: 8px; border-bottom: 1px solid #eee;">{eYear}</td></tr>
                    <tr><td style="padding: 8px; border-bottom: 1px solid #eee; color: #666;">Release Title</td><td style="padding: 8px; border-bottom: 1px solid #eee;">{eReleaseTitle}</td></tr>
                    <tr><td style="padding: 8px; border-bottom: 1px solid #eee; color: #666;">Media Type</td><td style="padding: 8px; border-bottom: 1px solid #eee;">{eMediaType}</td></tr>
                    {(string.IsNullOrEmpty(contribution.Asin) ? "" : $"<tr><td style=\"padding: 8px; border-bottom: 1px solid #eee; color: #666;\">ASIN</td><td style=\"padding: 8px; border-bottom: 1px solid #eee;\">{eAsin}</td></tr>")}
                    {(string.IsNullOrEmpty(contribution.Upc) ? "" : $"<tr><td style=\"padding: 8px; border-bottom: 1px solid #eee; color: #666;\">UPC</td><td style=\"padding: 8px; border-bottom: 1px solid #eee;\">{eUpc}</td></tr>")}
                </table>
                <p><a href="{adminUrl}" style="display: inline-block; padding: 10px 20px; background-color: {BrandColor}; color: #ffffff; text-decoration: none; border-radius: 4px;">Review Contribution</a></p>
            """);

        var message = new MailgunMessage
        {
            To = [opts.AdminEmail],
            Subject = $"New Contribution: {title} — {contribution.ReleaseTitle}",
            Html = html,
            Tags = ["contribution-admin-notification"]
        };
        EnrichMessage(message, contribution, "contribution-submitted", replyTo: userEmail);

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

        var html = WrapInEmailLayout($"""
                <h2 style="color: {BrandColor}; margin-top: 0;">Thanks for Your Contribution!</h2>
                <p>We've received your submission and an admin will review it shortly.</p>
                <table style="width: 100%; border-collapse: collapse; margin: 16px 0;">
                    <tr><td style="padding: 8px; border-bottom: 1px solid #eee; color: #666;">Title</td><td style="padding: 8px; border-bottom: 1px solid #eee;"><strong>{eTitle}</strong></td></tr>
                    <tr><td style="padding: 8px; border-bottom: 1px solid #eee; color: #666;">Year</td><td style="padding: 8px; border-bottom: 1px solid #eee;">{eYear}</td></tr>
                    <tr><td style="padding: 8px; border-bottom: 1px solid #eee; color: #666;">Release Title</td><td style="padding: 8px; border-bottom: 1px solid #eee;">{eReleaseTitle}</td></tr>
                    <tr><td style="padding: 8px; border-bottom: 1px solid #eee; color: #666;">Media Type</td><td style="padding: 8px; border-bottom: 1px solid #eee;">{eMediaType}</td></tr>
                </table>
                <p style="color: #666; font-size: 14px;">You'll receive updates about your contribution's status through <a href="https://thediscdb.com/messages" style="color: {BrandColor}; text-decoration: none;">TheDiscDb messages</a>.</p>
                <p style="color: #999; font-size: 12px;">If you didn't submit this contribution, please ignore this email.</p>
            """);

        var message = new MailgunMessage
        {
            To = [userEmail],
            Subject = $"Your contribution has been submitted — {title}",
            Html = html,
            Tags = ["contribution-user-confirmation"]
        };
        EnrichMessage(message, contribution, "contribution-user-confirmation", replyTo: opts.AdminEmail);

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

        var opts = options.CurrentValue;

        var title = $"{contribution.Title} ({contribution.Year})";
        var mediaType = contribution.MediaType?.ToLowerInvariant() ?? "movie";
        var hasItemLink = !string.IsNullOrEmpty(contribution.TitleSlug);
        var itemUrl = hasItemLink ? $"https://thediscdb.com/{mediaType}/{contribution.TitleSlug}" : "https://thediscdb.com";

        var eTitle = E(contribution.Title);
        var eYear = E(contribution.Year);
        var eReleaseTitle = E(contribution.ReleaseTitle);
        var eMediaType = E(contribution.MediaType);

        var linkHtml = hasItemLink
            ? $"""<p><a href="{itemUrl}" style="display: inline-block; padding: 10px 20px; background-color: {BrandColor}; color: #ffffff; text-decoration: none; border-radius: 4px;">View on TheDiscDb</a></p>"""
            : $"""<p><a href="{itemUrl}" style="display: inline-block; padding: 10px 20px; background-color: {BrandColor}; color: #ffffff; text-decoration: none; border-radius: 4px;">Visit TheDiscDb</a></p>""";

        var html = WrapInEmailLayout($"""
                <h2 style="color: {BrandColor}; margin-top: 0;">Your Contribution Has Been Imported!</h2>
                <p>Great news — your contribution has been reviewed, approved, and imported into TheDiscDb.</p>
                <table style="width: 100%; border-collapse: collapse; margin: 16px 0;">
                    <tr><td style="padding: 8px; border-bottom: 1px solid #eee; color: #666;">Title</td><td style="padding: 8px; border-bottom: 1px solid #eee;"><strong>{eTitle}</strong></td></tr>
                    <tr><td style="padding: 8px; border-bottom: 1px solid #eee; color: #666;">Year</td><td style="padding: 8px; border-bottom: 1px solid #eee;">{eYear}</td></tr>
                    <tr><td style="padding: 8px; border-bottom: 1px solid #eee; color: #666;">Release</td><td style="padding: 8px; border-bottom: 1px solid #eee;">{eReleaseTitle}</td></tr>
                    <tr><td style="padding: 8px; border-bottom: 1px solid #eee; color: #666;">Type</td><td style="padding: 8px; border-bottom: 1px solid #eee;">{eMediaType}</td></tr>
                </table>
                {linkHtml}
                <p style="color: #666; font-size: 14px;">Thank you for contributing to TheDiscDb! Your submission helps build the most complete disc database on the web.</p>
            """);

        var message = new MailgunMessage
        {
            To = [userEmail],
            Subject = $"Your contribution has been imported — {title}",
            Html = html,
            Tags = ["contribution-imported"]
        };
        EnrichMessage(message, contribution, "contribution-imported", replyTo: opts.AdminEmail);

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
        var renderedMessage = RenderMarkdown(message);

        var html = WrapInEmailLayout($"""
                <h2 style="color: {BrandColor}; margin-top: 0;">New Message</h2>
                <p><strong>{eName}</strong>{(string.IsNullOrEmpty(userEmail) ? "" : $" ({eEmail})")} sent a message on <strong>{eTitle}</strong> — {eRelease}:</p>
                <div style="margin: 16px 0; padding: 12px 16px; background: #f5f5f5; border-left: 4px solid {BrandColor};">{renderedMessage}</div>
                <p><a href="{adminUrl}" style="display: inline-block; padding: 10px 20px; background-color: {BrandColor}; color: #ffffff; text-decoration: none; border-radius: 4px;">View Message</a></p>
            """);

        var email = new MailgunMessage
        {
            To = [opts.AdminEmail],
            Subject = $"Message from {userName ?? "contributor"}: {title} — {contribution.ReleaseTitle}",
            Html = html,
            Tags = ["message-admin-notification"]
        };
        EnrichMessage(email, contribution, "message-from-user", replyTo: userEmail);

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

        var opts = options.CurrentValue;

        var title = $"{contribution.Title} ({contribution.Year})";
        var encodedId = idEncoder.Encode(contribution.Id);
        var messagesUrl = $"https://thediscdb.com/contribution/{encodedId}/messages";
        var eTitle = E(title);
        var eRelease = E(contribution.ReleaseTitle);
        var renderedMessage = RenderMarkdown(message);

        var html = WrapInEmailLayout($"""
                <h2 style="color: {BrandColor}; margin-top: 0;">New Message from TheDiscDb</h2>
                <p>An admin sent you a message about your contribution <strong>{eTitle}</strong> — {eRelease}:</p>
                <div style="margin: 16px 0; padding: 12px 16px; background: #f5f5f5; border-left: 4px solid {BrandColor};">{renderedMessage}</div>
                <p><a href="{messagesUrl}" style="display: inline-block; padding: 10px 20px; background-color: {BrandColor}; color: #ffffff; text-decoration: none; border-radius: 4px;">View Messages</a></p>
            """);

        var email = new MailgunMessage
        {
            To = [userEmail],
            Subject = $"Message about your contribution: {title}",
            Html = html,
            Tags = ["message-user-notification"]
        };
        EnrichMessage(email, contribution, "message-from-admin", replyTo: opts.AdminEmail);

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

    private const string BrandColor = "#00213F";
    private const string LogoUrl = "https://thediscdb.com/nav-logo.png";

    /// <summary>
    /// Wraps email body content in a branded layout with logo header and footer.
    /// </summary>
    private static string WrapInEmailLayout(string bodyContent)
    {
        return $"""
            <div style="font-family: -apple-system, BlinkMacSystemFont, 'Segoe UI', Roboto, sans-serif; max-width: 600px; margin: 0 auto; background: #ffffff;">
                <div style="background-color: {BrandColor}; padding: 20px 24px; text-align: center;">
                    <a href="https://thediscdb.com" style="text-decoration: none;">
                        <img src="{LogoUrl}" alt="TheDiscDb" style="height: 36px; vertical-align: middle;" />
                    </a>
                </div>
                <div style="padding: 24px;">
                    {bodyContent}
                </div>
                <div style="padding: 16px 24px; border-top: 1px solid #eee; text-align: center;">
                    <p style="color: #999; font-size: 12px; margin: 0;">
                        <a href="https://thediscdb.com" style="color: #999; text-decoration: none;">TheDiscDb</a> — The community disc database
                    </p>
                </div>
            </div>
            """;
    }

    private static readonly MarkdownPipeline EmailMarkdownPipeline = new MarkdownPipelineBuilder()
        .DisableHtml()
        .UseAdvancedExtensions()
        .Build();

    /// <summary>
    /// Renders markdown to HTML for email bodies. Raw HTML in the input is escaped
    /// by DisableHtml() so user content can't inject scripts or tags.
    /// </summary>
    private static string RenderMarkdown(string? text)
    {
        if (string.IsNullOrEmpty(text))
            return string.Empty;

        return Markdown.ToHtml(text, EmailMarkdownPipeline);
    }

    /// <summary>
    /// Strips HTML tags to produce a plain-text fallback and applies common Mailgun
    /// enhancements: tracking, custom variables, List-Unsubscribe, and Reply-To.
    /// </summary>
    private void EnrichMessage(MailgunMessage message, UserContribution contribution, string notificationType, string? replyTo = null)
    {
        var opts = options.CurrentValue;

        // Plain-text fallback from HTML
        if (!string.IsNullOrEmpty(message.Html) && string.IsNullOrEmpty(message.Text))
        {
            message.Text = System.Text.RegularExpressions.Regex.Replace(message.Html, "<[^>]+>", "").Trim();
        }

        // Open and click tracking
        message.TrackingOpens = true;
        message.TrackingClicks = true;

        // Custom variables for webhook correlation
        message.CustomVariables["contribution-id"] = contribution.Id.ToString();
        message.CustomVariables["notification-type"] = notificationType;

        // List-Unsubscribe header (mailto-based, no infrastructure needed)
        message.CustomHeaders["List-Unsubscribe"] = $"<mailto:{opts.AdminEmail}?subject=Unsubscribe>";
        message.CustomHeaders["List-Unsubscribe-Post"] = "List-Unsubscribe=One-Click";

        // Reply-To
        if (!string.IsNullOrEmpty(replyTo))
        {
            message.ReplyTo = replyTo;
        }
    }
}
