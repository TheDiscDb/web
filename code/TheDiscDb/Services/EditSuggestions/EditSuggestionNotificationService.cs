namespace TheDiscDb.Services.EditSuggestions;

using System;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using Markdig;
using Microsoft.Extensions.Options;
using TheDiscDb.Services.Server;
using TheDiscDb.Web.Data;
using TheDiscDb.Web.Email;

/// <summary>
/// Mailgun-backed edit-suggestion notifications. Mirrors
/// <see cref="TheDiscDb.Services.ContributionNotificationService"/>: branded HTML
/// layout, markdown rendering for message bodies, and best-effort sends that log
/// and swallow failures so a mail problem never breaks the submit/review flow.
/// </summary>
public sealed class EditSuggestionNotificationService : IEditSuggestionNotificationService
{
    private readonly MailgunClient mailgun;
    private readonly IOptionsMonitor<MailgunOptions> options;
    private readonly IdEncoder idEncoder;
    private readonly ILogger<EditSuggestionNotificationService> logger;

    public EditSuggestionNotificationService(
        MailgunClient mailgun,
        IOptionsMonitor<MailgunOptions> options,
        IdEncoder idEncoder,
        ILogger<EditSuggestionNotificationService> logger)
    {
        this.mailgun = mailgun;
        this.options = options;
        this.idEncoder = idEncoder;
        this.logger = logger;
    }

    public async Task NotifySuggestionSubmittedAsync(EditSuggestion suggestion, string? userEmail, string? userName, CancellationToken cancellationToken = default)
    {
        var opts = options.CurrentValue;
        var displayName = userName ?? "A user";
        var label = DescribeSuggestion(suggestion);

        // Admin notification
        if (string.IsNullOrEmpty(opts.AdminEmail))
        {
            logger.LogWarning("Mailgun AdminEmail is not configured — skipping admin notification for edit suggestion {Id}", suggestion.Id);
        }
        else
        {
            var adminUrl = AdminUrl(suggestion);
            var html = WrapInEmailLayout($"""
                    <h2 style="color: {BrandColor}; margin-top: 0;">New Edit Suggestion</h2>
                    <p><strong>{E(displayName)}</strong>{(string.IsNullOrEmpty(userEmail) ? "" : $" ({E(userEmail)})")} submitted an edit suggestion.</p>
                    {SuggestionDetailsTable(suggestion)}
                    <p><a href="{adminUrl}" style="display: inline-block; padding: 10px 20px; background-color: {BrandColor}; color: #ffffff; text-decoration: none; border-radius: 4px;">Review Suggestion</a></p>
                """);

            var message = new MailgunMessage
            {
                To = [opts.AdminEmail],
                Subject = $"New Edit Suggestion: {label}",
                Html = html,
                Tags = ["edit-suggestion-admin-notification"]
            };
            EnrichMessage(message, suggestion, "edit-suggestion-submitted", replyTo: userEmail);
            await SendAsync(message, suggestion.Id, "admin submitted notification", cancellationToken);
        }

        // User confirmation
        if (string.IsNullOrEmpty(userEmail))
        {
            logger.LogDebug("No user email available — skipping user confirmation for edit suggestion {Id}", suggestion.Id);
            return;
        }

        var confirmHtml = WrapInEmailLayout($"""
                <h2 style="color: {BrandColor}; margin-top: 0;">Thanks for Your Suggestion!</h2>
                <p>We've received your edit suggestion and an admin will review it shortly.</p>
                {SuggestionDetailsTable(suggestion)}
                <p style="color: #666; font-size: 14px;">You'll be able to follow its status on <a href="{UserUrl(suggestion)}" style="color: {BrandColor}; text-decoration: none;">your changes page</a>.</p>
            """);

        var confirm = new MailgunMessage
        {
            To = [userEmail],
            Subject = $"Your edit suggestion has been submitted — {label}",
            Html = confirmHtml,
            Tags = ["edit-suggestion-user-confirmation"]
        };
        EnrichMessage(confirm, suggestion, "edit-suggestion-user-confirmation", replyTo: opts.AdminEmail);
        await SendAsync(confirm, suggestion.Id, "user confirmation", cancellationToken);
    }

    public async Task NotifySuggestionResolvedAsync(EditSuggestion suggestion, string? userEmail, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(userEmail))
        {
            logger.LogDebug("No user email available — skipping resolved notification for edit suggestion {Id}", suggestion.Id);
            return;
        }

        var opts = options.CurrentValue;
        var label = DescribeSuggestion(suggestion);

        var (heading, intro, subjectVerb) = suggestion.Status switch
        {
            EditSuggestionStatus.Approved => (
                "Your Suggestion Was Approved!",
                "Great news — your edit suggestion has been reviewed and approved.",
                "was approved"),
            EditSuggestionStatus.Rejected => (
                "Your Suggestion Wasn't Accepted",
                "Thanks for your edit suggestion. After review it wasn't accepted this time — see the details below.",
                "wasn't accepted"),
            _ => (
                "Your Suggestion Was Partially Approved",
                "Thanks for your edit suggestion. Some of your changes were approved and others weren't — see the details below.",
                "was partially approved"),
        };

        var html = WrapInEmailLayout($"""
                <h2 style="color: {BrandColor}; margin-top: 0;">{E(heading)}</h2>
                <p>{E(intro)}</p>
                {ChangeOutcomeTable(suggestion)}
                <p><a href="{UserUrl(suggestion)}" style="display: inline-block; padding: 10px 20px; background-color: {BrandColor}; color: #ffffff; text-decoration: none; border-radius: 4px;">View Your Suggestion</a></p>
                <p style="color: #666; font-size: 14px;">Thank you for helping improve TheDiscDb!</p>
            """);

        var message = new MailgunMessage
        {
            To = [userEmail],
            Subject = $"Your edit suggestion {subjectVerb} — {label}",
            Html = html,
            Tags = ["edit-suggestion-resolved"]
        };
        EnrichMessage(message, suggestion, "edit-suggestion-resolved", replyTo: opts.AdminEmail);
        await SendAsync(message, suggestion.Id, "resolved notification", cancellationToken);
    }

    public async Task NotifyMessageFromUserAsync(EditSuggestion suggestion, string message, string? userName, string? userEmail, CancellationToken cancellationToken = default)
    {
        var opts = options.CurrentValue;
        if (string.IsNullOrEmpty(opts.AdminEmail))
        {
            logger.LogWarning("Mailgun AdminEmail is not configured — skipping message notification for edit suggestion {Id}", suggestion.Id);
            return;
        }

        var label = DescribeSuggestion(suggestion);
        var html = WrapInEmailLayout($"""
                <h2 style="color: {BrandColor}; margin-top: 0;">New Message</h2>
                <p><strong>{E(userName ?? "A user")}</strong>{(string.IsNullOrEmpty(userEmail) ? "" : $" ({E(userEmail)})")} sent a message on edit suggestion <strong>{E(label)}</strong>:</p>
                <div style="margin: 16px 0; padding: 12px 16px; background: #f5f5f5; border-left: 4px solid {BrandColor};">{RenderMarkdown(message)}</div>
                <p><a href="{AdminUrl(suggestion)}" style="display: inline-block; padding: 10px 20px; background-color: {BrandColor}; color: #ffffff; text-decoration: none; border-radius: 4px;">View Message</a></p>
            """);

        var email = new MailgunMessage
        {
            To = [opts.AdminEmail],
            Subject = $"Message from {userName ?? "a user"}: edit suggestion {label}",
            Html = html,
            Tags = ["edit-suggestion-message-admin-notification"]
        };
        EnrichMessage(email, suggestion, "edit-suggestion-message-from-user", replyTo: userEmail);
        await SendAsync(email, suggestion.Id, "user message notification", cancellationToken);
    }

    public async Task NotifyMessageFromAdminAsync(EditSuggestion suggestion, string message, string? userEmail, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(userEmail))
        {
            logger.LogDebug("No user email available — skipping admin message notification for edit suggestion {Id}", suggestion.Id);
            return;
        }

        var opts = options.CurrentValue;
        var label = DescribeSuggestion(suggestion);
        var html = WrapInEmailLayout($"""
                <h2 style="color: {BrandColor}; margin-top: 0;">New Message from TheDiscDb</h2>
                <p>An admin sent you a message about your edit suggestion <strong>{E(label)}</strong>:</p>
                <div style="margin: 16px 0; padding: 12px 16px; background: #f5f5f5; border-left: 4px solid {BrandColor};">{RenderMarkdown(message)}</div>
                <p><a href="{UserUrl(suggestion)}" style="display: inline-block; padding: 10px 20px; background-color: {BrandColor}; color: #ffffff; text-decoration: none; border-radius: 4px;">View Messages</a></p>
            """);

        var email = new MailgunMessage
        {
            To = [userEmail],
            Subject = $"Message about your edit suggestion: {label}",
            Html = html,
            Tags = ["edit-suggestion-message-user-notification"]
        };
        EnrichMessage(email, suggestion, "edit-suggestion-message-from-admin", replyTo: opts.AdminEmail);
        await SendAsync(email, suggestion.Id, "admin message notification", cancellationToken);
    }

    private async Task SendAsync(MailgunMessage message, int suggestionId, string description, CancellationToken cancellationToken)
    {
        try
        {
            await mailgun.SendAsync(message, cancellationToken);
            logger.LogInformation("Sent edit-suggestion {Description} for suggestion {Id}", description, suggestionId);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to send edit-suggestion {Description} for suggestion {Id}", description, suggestionId);
        }
    }

    private static string DescribeSuggestion(EditSuggestion suggestion)
        => !string.IsNullOrWhiteSpace(suggestion.Summary) ? suggestion.Summary!
            : !string.IsNullOrWhiteSpace(suggestion.TargetEntityKey) ? suggestion.TargetEntityKey!
            : $"#{suggestion.Id}";

    private string AdminUrl(EditSuggestion suggestion) => $"https://thediscdb.com/admin/changes/{suggestion.Id}";

    private string UserUrl(EditSuggestion suggestion) => $"https://thediscdb.com/changes/my/{idEncoder.Encode(suggestion.Id)}";

    private static string SuggestionDetailsTable(EditSuggestion suggestion)
    {
        var changeCount = suggestion.Changes?.Count ?? 0;
        return $"""
            <table style="width: 100%; border-collapse: collapse; margin: 16px 0;">
                <tr><td style="padding: 8px; border-bottom: 1px solid #eee; color: #666;">Summary</td><td style="padding: 8px; border-bottom: 1px solid #eee;"><strong>{E(DescribeSuggestion(suggestion))}</strong></td></tr>
                <tr><td style="padding: 8px; border-bottom: 1px solid #eee; color: #666;">Target</td><td style="padding: 8px; border-bottom: 1px solid #eee;">{E(suggestion.TargetEntityType)}{(string.IsNullOrEmpty(suggestion.TargetEntityKey) ? "" : $" — {E(suggestion.TargetEntityKey)}")}</td></tr>
                <tr><td style="padding: 8px; border-bottom: 1px solid #eee; color: #666;">Changes</td><td style="padding: 8px; border-bottom: 1px solid #eee;">{changeCount}</td></tr>
            </table>
            """;
    }

    private static string ChangeOutcomeTable(EditSuggestion suggestion)
    {
        var rows = (suggestion.Changes ?? Array.Empty<EditSuggestionChange>())
            .OrderBy(c => c.Ordinal)
            .Select(c =>
            {
                var note = c.ConflictReason ?? c.AdminNote;
                var noteCell = string.IsNullOrWhiteSpace(note) ? "" : $" — {E(note)}";
                return $"""<tr><td style="padding: 8px; border-bottom: 1px solid #eee;">{E(c.Type)}</td><td style="padding: 8px; border-bottom: 1px solid #eee;"><strong>{E(DescribeChangeStatus(c.Status))}</strong>{noteCell}</td></tr>""";
            });

        return $"""
            <table style="width: 100%; border-collapse: collapse; margin: 16px 0;">
                <tr><td style="padding: 8px; border-bottom: 2px solid #ddd; color: #666;">Change</td><td style="padding: 8px; border-bottom: 2px solid #ddd; color: #666;">Outcome</td></tr>
                {string.Concat(rows)}
            </table>
            """;
    }

    private static string DescribeChangeStatus(EditSuggestionChangeStatus status) => status switch
    {
        EditSuggestionChangeStatus.Applied => "Approved",
        EditSuggestionChangeStatus.Rejected => "Not accepted",
        EditSuggestionChangeStatus.Conflicted => "Needs attention",
        EditSuggestionChangeStatus.Pending => "Pending",
        _ => status.ToString(),
    };

    private static string E(string? value) => WebUtility.HtmlEncode(value ?? string.Empty);

    private const string BrandColor = "#00213F";
    private const string LogoUrl = "https://thediscdb.com/nav-logo.png";

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

    private static string RenderMarkdown(string? text)
        => string.IsNullOrEmpty(text) ? string.Empty : Markdown.ToHtml(text, EmailMarkdownPipeline);

    private void EnrichMessage(MailgunMessage message, EditSuggestion suggestion, string notificationType, string? replyTo = null)
    {
        var opts = options.CurrentValue;

        if (!string.IsNullOrEmpty(message.Html) && string.IsNullOrEmpty(message.Text))
        {
            message.Text = System.Text.RegularExpressions.Regex.Replace(message.Html, "<[^>]+>", "").Trim();
        }

        message.TrackingOpens = true;
        message.TrackingClicks = true;

        message.CustomVariables["edit-suggestion-id"] = suggestion.Id.ToString();
        message.CustomVariables["notification-type"] = notificationType;

        message.CustomHeaders["List-Unsubscribe"] = $"<mailto:{opts.AdminEmail}?subject=Unsubscribe>";
        message.CustomHeaders["List-Unsubscribe-Post"] = "List-Unsubscribe=One-Click";

        if (!string.IsNullOrEmpty(replyTo))
        {
            message.ReplyTo = replyTo;
        }
    }
}
