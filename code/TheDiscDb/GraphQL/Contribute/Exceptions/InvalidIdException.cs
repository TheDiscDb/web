namespace TheDiscDb.GraphQL.Contribute.Exceptions;

public class InvalidIdException : Exception
{
    public InvalidIdException(string id, string type)
        : base($"Invalid {type} ID '{id}'")
    {
    }
}