using Microsoft.AspNetCore.Authentication;

namespace TheDiscDb.Web.Authentication;

public class ApiKeyAuthenticationOptions : AuthenticationSchemeOptions
{
    public bool IsEnabled { get; set; }
}

public static class ApiKeyAuthenticationDefaults
{
    public const string Scheme = "ApiKey";
    public const string PolicyName = "GraphQLPolicy";
    public const string ConfigSection = "GraphQL:ApiKeyAuthentication";
}
