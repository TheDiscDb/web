namespace TheDiscDb.Web.Data;

using System;
using System.Collections.Generic;

/// <summary>
/// A bundle of one or more proposed changes to existing data, submitted by a user
/// and reviewed by an admin. Each change inside the bundle is independently
/// approvable / rejectable; the bundle status is a roll-up.
/// </summary>
public class EditSuggestion : IHasId
{
    public int Id { get; set; }

    public string UserId { get; set; } = string.Empty;

    public DateTimeOffset Created { get; set; }

    public EditSuggestionStatus Status { get; set; } = EditSuggestionStatus.Pending;

    /// <summary>Optional user-supplied title for the bundle.</summary>
    public string? Summary { get; set; }

    /// <summary>
    /// Denormalised for queue filtering and "show suggestions affecting this entity"
    /// lookups. Values are the entity type names: <c>Release</c>, <c>Disc</c>, <c>DiscItem</c>.
    /// </summary>
    public string TargetEntityType { get; set; } = string.Empty;

    /// <summary>
    /// Denormalised primary entity id for display / grouping. Individual changes
    /// inside the bundle may target child entities of this one.
    /// </summary>
    public int TargetEntityId { get; set; }

    public string? ReviewedByUserId { get; set; }

    public DateTimeOffset? ReviewedAt { get; set; }

    public EditSuggestionSource Source { get; set; } = EditSuggestionSource.Web;

    public ICollection<EditSuggestionChange> Changes { get; set; } = new HashSet<EditSuggestionChange>();
}
