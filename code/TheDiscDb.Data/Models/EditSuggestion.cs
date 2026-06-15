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
    /// Stable natural-key identifier of the primary target entity. Format depends
    /// on <see cref="TargetEntityType"/>; for <c>Release</c> it is
    /// <c>"&lt;parentSlug&gt;/&lt;releaseSlug&gt;"</c> where the parent slug
    /// references either a MediaItem or a Boxset. Slugs are used (not the int
    /// primary key) because the non-user data tables are designed to be
    /// truncated and rebuilt from the file repo at any time, which shifts every
    /// int id but preserves slugs (slugs are non-editable and appear in public
    /// URLs). The exact mapping back to the typed parent + child slug lives in
    /// the individual change's Details JSON.
    /// </summary>
    public string? TargetEntityKey { get; set; }

    public string? ReviewedByUserId { get; set; }

    public DateTimeOffset? ReviewedAt { get; set; }

    public EditSuggestionSource Source { get; set; } = EditSuggestionSource.Web;

    public ICollection<EditSuggestionChange> Changes { get; set; } = new HashSet<EditSuggestionChange>();
}
