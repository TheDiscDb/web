namespace TheDiscDb.GraphQL.Contribute.Exceptions;

public class InvalidContributionStatusException : Exception
{
    public InvalidContributionStatusException(string status)
        : base($"Contribution cannot be deleted in '{status}' status. Only Pending, Rejected, or ChangesRequested contributions can be deleted.")
    {
    }
}
