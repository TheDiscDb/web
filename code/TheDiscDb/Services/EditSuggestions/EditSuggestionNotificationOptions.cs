namespace TheDiscDb.Services.EditSuggestions;

/// <summary>
/// Controls which edit-suggestion email audiences are notified. This is separate
/// from Mailgun configuration: even when Mailgun is fully configured, no
/// edit-suggestion mail is sent unless the relevant audience switch below is
/// <c>true</c>. Splitting admins from users lets us, for example, email the admin
/// on every new suggestion while keeping users silent until we choose to enable
/// user-facing mail. Both default to <c>false</c>, so the feature ships dormant
/// and every notification call site stays harmless until turned on.
/// Bind from configuration section <c>EditSuggestions:Notifications</c>.
/// </summary>
public sealed class EditSuggestionNotificationOptions
{
    /// <summary>
    /// When <c>true</c>, send admin-facing notifications: a new suggestion was
    /// submitted, and when a user replies on a suggestion. Routed to
    /// <c>Mailgun:AdminEmail</c>.
    /// </summary>
    public bool NotifyAdmins { get; set; }

    /// <summary>
    /// When <c>true</c>, send user-facing notifications: the "we received your
    /// suggestion" confirmation, the final approve/reject/partial outcome, and
    /// replies from an admin. Routed to the suggester's email.
    /// </summary>
    public bool NotifyUsers { get; set; }

    /// <summary>True when at least one audience is enabled.</summary>
    public bool AnyEnabled => this.NotifyAdmins || this.NotifyUsers;
}
