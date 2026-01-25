namespace TheDiscDb.GraphQL.Contribute.Exceptions;

public class InvalidIdException : Exception
{
    public InvalidIdException(string id, string type)
        : base($"Invalid {type} ID '{id}'")
    {
    }
}

public class InvalidOwnershipException : Exception
{
    public InvalidOwnershipException(string id, string type)
        : base($"User does not own {type} with ID '{id}'")
    {
    }
}