namespace TheDiscDb.Services.Contributions;

public class UnsupportedExternalProviderException : Exception
{
    public UnsupportedExternalProviderException(string provider)
        : base($"External provider '{provider}' is not supported. TMDB is the only supported provider.")
    {
    }
}
