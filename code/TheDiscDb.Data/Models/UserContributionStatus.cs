namespace TheDiscDb.Web.Data;

public enum UserContributionStatus
{
    Pending,
    ReadyForReview,
    Approved,
    ChangesRequested,
    Rejected,
    Imported
}

public static class UserContributionStatusExtensions
{
    /// <summary>
    /// True when the owner of a contribution / boxset can edit it. Pending = first draft.
    /// ChangesRequested + Rejected = admin asked the user to revise; the user needs to be
    /// able to edit and re-submit.
    /// </summary>
    public static bool IsEditableByOwner(this UserContributionStatus status) => status is
        UserContributionStatus.Pending or
        UserContributionStatus.ChangesRequested or
        UserContributionStatus.Rejected;
}
