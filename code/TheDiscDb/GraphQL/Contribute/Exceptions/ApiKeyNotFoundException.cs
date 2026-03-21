namespace TheDiscDb.GraphQL.Contribute.Exceptions;

public class ApiKeyNotFoundException : Exception
{
    public ApiKeyNotFoundException(int id) : base($"API key with ID {id} was not found.") { }
}
