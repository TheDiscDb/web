using System;
using System.Collections.Generic;

namespace TheDiscDb.Web.Email;

public class MailgunMessage
{
    /// <summary>
    /// Sender address. If null, <see cref="MailgunOptions.FromEmail"/> is used.
    /// Supports friendly-name format: "Display Name &lt;email@domain.com&gt;".
    /// </summary>
    public string? From { get; set; }

    /// <summary>
    /// Primary recipient(s). At least one is required.
    /// </summary>
    public List<string> To { get; set; } = [];

    /// <summary>
    /// Carbon-copy recipient(s).
    /// </summary>
    public List<string> Cc { get; set; } = [];

    /// <summary>
    /// Blind carbon-copy recipient(s).
    /// </summary>
    public List<string> Bcc { get; set; } = [];

    /// <summary>
    /// Email subject line.
    /// </summary>
    public string Subject { get; set; } = string.Empty;

    /// <summary>
    /// Plain-text body. At least one of <see cref="Text"/> or <see cref="Html"/> must be provided.
    /// </summary>
    public string? Text { get; set; }

    /// <summary>
    /// HTML body. At least one of <see cref="Text"/> or <see cref="Html"/> must be provided.
    /// </summary>
    public string? Html { get; set; }

    /// <summary>
    /// Reply-To address. Sent as the h:Reply-To header.
    /// </summary>
    public string? ReplyTo { get; set; }

    /// <summary>
    /// Tags for categorization (max 3 per message in Mailgun).
    /// </summary>
    public List<string> Tags { get; set; } = [];

    /// <summary>
    /// Enable or disable open/click tracking. Null = use domain default.
    /// </summary>
    public bool? Tracking { get; set; }

    /// <summary>
    /// Enable or disable click tracking specifically. Null = use domain default.
    /// </summary>
    public bool? TrackingClicks { get; set; }

    /// <summary>
    /// Enable or disable open tracking specifically. Null = use domain default.
    /// </summary>
    public bool? TrackingOpens { get; set; }

    /// <summary>
    /// Send in test mode (message accepted but not delivered).
    /// </summary>
    public bool TestMode { get; set; }

    /// <summary>
    /// Scheduled delivery time. Max 3–7 days in the future depending on plan.
    /// </summary>
    public DateTimeOffset? DeliveryTime { get; set; }

    /// <summary>
    /// Require TLS for delivery. If TLS cannot be established, message is not delivered.
    /// </summary>
    public bool? RequireTls { get; set; }

    /// <summary>
    /// Custom MIME headers. Keys should NOT include the "h:" prefix — it is added automatically.
    /// Example: { "X-My-Header", "value" } → sent as h:X-My-Header.
    /// </summary>
    public Dictionary<string, string> CustomHeaders { get; set; } = [];

    /// <summary>
    /// Custom variables attached to the message. Keys should NOT include the "v:" prefix.
    /// Values are sent as JSON-encoded strings.
    /// </summary>
    public Dictionary<string, string> CustomVariables { get; set; } = [];
}
