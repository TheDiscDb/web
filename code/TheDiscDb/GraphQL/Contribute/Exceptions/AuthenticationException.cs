namespace TheDiscDb.GraphQL.Contribute.Exceptions;

public class AuthenticationException : Exception
{
    public AuthenticationException(string message)
        : base(message)
    {
    }
}
