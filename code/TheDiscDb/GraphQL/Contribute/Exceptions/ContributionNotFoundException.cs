namespace TheDiscDb.GraphQL.Contribute.Exceptions;

public class ContributionNotFoundException : Exception
{
    public ContributionNotFoundException(string encodedId) 
        : base($"Contribution with id {encodedId} not found")
    {
    }
}
