namespace TheDiscDb.GraphQL.Contribute.Exceptions;

public class InvalidContributionStatusException : Exception
{
    public InvalidContributionStatusException(string status)
        : base($"This operation is not allowed for contributions with status '{status}'. Only Pending, Rejected, or ChangesRequested contributions can be modified.")
    {
    }

    public InvalidContributionStatusException(string status, string operation)
        : base($"Contribution cannot be {operation} in '{status}' status. Only Pending, Rejected, or ChangesRequested contributions can be {operation}.")
    {
    }
}
