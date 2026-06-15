namespace TheDiscDb.Web.Data;

using System;

/// <summary>
/// Admin-to-user / user-to-admin message attached to an <see cref="EditSuggestion"/>.
/// Mirrors the shape of <see cref="UserMessage"/> but keyed to EditSuggestion so the
/// two threading systems do not interfere.
/// </summary>
public class EditSuggestionMessage
{
    public int Id { get; set; }

    public int SuggestionId { get; set; }

    public EditSuggestion? Suggestion { get; set; }

    public string FromUserId { get; set; } = string.Empty;

    public string ToUserId { get; set; } = string.Empty;

    public string Message { get; set; } = string.Empty;

    public bool IsRead { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
}
