namespace TheDiscDb.GraphQL.Contribute.Exceptions;

public class ContributionAlreadyInBoxsetException : Exception
{
    public ContributionAlreadyInBoxsetException(string contributionId)
        : base($"Contribution '{contributionId}' is already a member of a boxset")
    {
    }
}
