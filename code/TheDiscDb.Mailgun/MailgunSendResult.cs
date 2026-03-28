namespace TheDiscDb.Web.Email;

/// <summary>
/// Successful response from the Mailgun Messages API.
/// </summary>
public class MailgunSendResult
{
    /// <summary>
    /// Message ID assigned by Mailgun (e.g. "&lt;message-id@domain.com&gt;").
    /// </summary>
    public string Id { get; set; } = string.Empty;

    /// <summary>
    /// Status message (e.g. "Queued. Thank you.").
    /// </summary>
    public string Message { get; set; } = string.Empty;
}
