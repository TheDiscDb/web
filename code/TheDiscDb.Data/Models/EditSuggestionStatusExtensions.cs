namespace TheDiscDb.Web.Data;

public static class EditSuggestionStatusExtensions
{
    /// <summary>
    /// True when an admin can still act on the suggestion (approve / reject /
    /// resolve its changes). The terminal states — <see cref="EditSuggestionStatus.Draft"/>,
    /// <see cref="EditSuggestionStatus.Approved"/>, <see cref="EditSuggestionStatus.Rejected"/>
    /// and <see cref="EditSuggestionStatus.Withdrawn"/> — are not reviewable, even
    /// though individual child changes may still carry a Pending status (e.g. a
    /// user withdrew a suggestion before any change was actioned).
    /// </summary>
    public static bool IsReviewable(this EditSuggestionStatus status)
        => status is EditSuggestionStatus.Pending
            or EditSuggestionStatus.InReview
            or EditSuggestionStatus.PartiallyApproved
            or EditSuggestionStatus.Conflicted;
}
