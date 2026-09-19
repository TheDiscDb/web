namespace TheDiscDb.Web.Data;

public enum EditSuggestionStatus
{
    Draft,
    Pending,
    InReview,
    ChangesRequested,
    PartiallyApproved,
    Approved,
    Rejected,
    Conflicted,
    Withdrawn
}
