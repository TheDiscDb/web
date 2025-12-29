namespace TheDiscDb.GraphQL.Contribute.Exceptions;

public class DiscItemNotFoundException : NotFoundException
{
    public DiscItemNotFoundException(string encodedId)
        : base(encodedId, "Disc Item")
    {
    }
}
