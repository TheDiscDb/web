namespace TheDiscDb.GraphQL.Contribute.Exceptions;

public class InvalidBoxsetStatusException : Exception
{
    public InvalidBoxsetStatusException(string status, string action)
        : base($"Boxset in '{status}' status cannot be {action}.")
    {
    }
}
