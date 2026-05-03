namespace TheDiscDb.Client.Contributions;

/// <summary>
/// Mirror of TheDiscDb.Web.Data.UserContributionStatusExtensions for the StrawberryShake-generated
/// client enum. Server and client carry their own enum types but share the same status semantics.
/// Keep these two helpers in sync.
/// </summary>
public static class UserContributionStatusClientExtensions
{
    public static bool IsEditableByOwner(this UserContributionStatus status) => status is
        UserContributionStatus.Pending or
        UserContributionStatus.ChangesRequested or
        UserContributionStatus.Rejected;
}
