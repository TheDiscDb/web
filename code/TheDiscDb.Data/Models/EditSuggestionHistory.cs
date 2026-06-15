namespace TheDiscDb.Web.Data;

using System;

/// <summary>
/// Append-only audit log entry for an <see cref="EditSuggestion"/>. Mirrors the
/// existing <see cref="ContributionHistory"/> pattern.
/// </summary>
public class EditSuggestionHistory
{
    public int Id { get; set; }

    public int SuggestionId { get; set; }

    /// <summary>Set when the entry is about a specific change within the bundle.</summary>
    public int? ChangeId { get; set; }

    public DateTimeOffset TimeStamp { get; set; }

    public string Description { get; set; } = string.Empty;

    public string UserId { get; set; } = string.Empty;

    public EditSuggestionHistoryType Type { get; set; }
}
