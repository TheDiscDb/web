namespace TheDiscDb.GraphQL.Contribute.Exceptions;

public class LogsNotFoundException : NotFoundException
{
    public LogsNotFoundException(string encodedId)
        : base(encodedId, "Logs")
    {
    }
}
