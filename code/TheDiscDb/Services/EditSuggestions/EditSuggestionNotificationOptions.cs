namespace TheDiscDb.Services.EditSuggestions;

/// <summary>
/// Controls whether edit-suggestion email notifications are sent. This is a
/// separate switch from Mailgun configuration: even when Mailgun is fully
/// configured, edit-suggestion notifications stay OFF unless <see cref="Enabled"/>
/// is explicitly set to <c>true</c>. This lets us wire every notification call
/// site without becoming chatty until we choose to turn them on.
/// Bind from configuration section <c>EditSuggestions:Notifications</c>.
/// </summary>
public sealed class EditSuggestionNotificationOptions
{
    public bool Enabled { get; set; }
}
