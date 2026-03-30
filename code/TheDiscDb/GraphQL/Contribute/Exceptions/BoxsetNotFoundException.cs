namespace TheDiscDb.GraphQL.Contribute.Exceptions;

public class BoxsetNotFoundException : NotFoundException
{
    public BoxsetNotFoundException(string encodedId)
        : base(encodedId, "Boxset")
    {
    }
}
