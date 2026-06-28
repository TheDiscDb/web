namespace TheDiscDb.Web.Data;

using System;

/// <summary>
/// A single proposed change inside an <see cref="EditSuggestion"/> bundle.
/// The <see cref="Type"/> string is the key used by ChangeFactory to materialise
/// the correct <c>IChange</c> implementation from the stored <see cref="ProposedJson"/>.
/// </summary>
public class EditSuggestionChange : IHasId
{
    public int Id { get; set; }

    public int SuggestionId { get; set; }

    public EditSuggestion? Suggestion { get; set; }

    /// <summary>Display order within the bundle.</summary>
    public int Ordinal { get; set; }

    /// <summary>ChangeFactory key, e.g. <c>release.fields.update</c>.</summary>
    public string Type { get; set; } = string.Empty;

    /// <summary>
    /// Snapshot of the target entity at edit time, used for diff rendering and
    /// stale-data conflict detection. Null for pure "add" changes that have no prior state.
    /// Stored as JSON in <c>nvarchar(max)</c>; can migrate to the native SQL Server
    /// <c>json</c> type once everywhere we run supports it.
    /// </summary>
    public string? OriginalSnapshotJson { get; set; }

    /// <summary>The proposed <c>*Details</c> payload, serialised as JSON.</summary>
    public string ProposedJson { get; set; } = string.Empty;

    public EditSuggestionChangeStatus Status { get; set; } = EditSuggestionChangeStatus.Pending;

    public DateTimeOffset? AppliedAt { get; set; }

    public string? AppliedByUserId { get; set; }

    /// <summary>
    /// Set when the change has been written to the <c>data/</c> repo by the batch
    /// sync tool. Null means the DB has been updated but the files have not.
    /// </summary>
    public DateTimeOffset? SyncedToFilesAt { get; set; }

    public string? ConflictReason { get; set; }

    public string? AdminNote { get; set; }
}
