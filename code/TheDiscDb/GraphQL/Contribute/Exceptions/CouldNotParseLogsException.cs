namespace TheDiscDb.GraphQL.Contribute.Exceptions;

public class CouldNotParseLogsException : Exception
{
    public CouldNotParseLogsException(string discId, Exception innerException)
        : base($"Could not parse logs for disc {discId}", innerException)
    {
    }
}