namespace TheDiscDb.GraphQL.Contribute.Exceptions;

public class DiscNotFoundException : NotFoundException
{
    public DiscNotFoundException(string encodedId)
        : base(encodedId, "Disc")
    {
    }
}
