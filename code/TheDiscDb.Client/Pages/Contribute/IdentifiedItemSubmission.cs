namespace TheDiscDb.Client.Pages.Contribute;

public enum ItemSubmissionKind
{
    Add,
    Edit
}

/// <summary>
/// Decides whether saving an identified disc item should update an existing row or add a new one.
/// An item that already carries a persisted database id must be edited in place; routing it to the
/// add path instead creates duplicate disc items (e.g. an original "before edit" row plus a new
/// "after edit" row), which then surface as duplicates in the naming helper.
/// </summary>
public static class IdentifiedItemSubmission
{
    public static ItemSubmissionKind GetSubmissionKind(string? databaseId)
        => databaseId is not null ? ItemSubmissionKind.Edit : ItemSubmissionKind.Add;
}
