namespace TheDiscDb.GraphQL.Contribute.Exceptions;

public class ContributionNotFoundException : NotFoundException
{
    public ContributionNotFoundException(string encodedId)
        : base(encodedId, "Contribution")
    {
    }
}
