namespace TheDiscDb.GraphQL.Contribute.Exceptions;

public class ExternalDataNotFoundException : Exception
{
    public ExternalDataNotFoundException(string externalId, string type) 
        : base($"{type} with TMDB ID {externalId} not found")
    {
    }
}
