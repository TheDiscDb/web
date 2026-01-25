namespace TheDiscDb.GraphQL.Contribute.Exceptions;

public abstract class NotFoundException : Exception
{
    protected NotFoundException(string encodedId, string type)
        : base($"{type} with id {encodedId} not found")
    {
    }
}
