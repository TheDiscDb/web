namespace TheDiscDb.GraphQL.Contribute.Exceptions;

public class FieldRequiredException : Exception
{
    public FieldRequiredException(string fieldName)
        : base($"{fieldName} is required")
    {
    }
}
