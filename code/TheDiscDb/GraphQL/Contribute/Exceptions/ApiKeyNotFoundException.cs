namespace TheDiscDb.GraphQL.Contribute.Exceptions;

public class ApiKeyNotFoundException : Exception
{
    public ApiKeyNotFoundException(string keyPrefix) : base($"API key with prefix '{keyPrefix}' was not found.") { }
}
