namespace TheDiscDb.GraphQL.Contribute.Exceptions;

public class ExistingDiscAlreadyInBoxsetException : Exception
{
    public ExistingDiscAlreadyInBoxsetException(string discPath)
        : base($"Existing disc '{discPath}' is already a member of this boxset")
    {
    }
}

public class InvalidDiscPathException : Exception
{
    public InvalidDiscPathException(string discPath)
        : base($"Invalid disc path format: '{discPath}'")
    {
    }
}
