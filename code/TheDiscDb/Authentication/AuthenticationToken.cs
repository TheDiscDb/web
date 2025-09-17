namespace TheDiscDb.Web.Authentication;

public class AuthenticationToken
{
    public string? Token { get; set; }
}

public class JwtOptions
{
    public string? Key { get; set; }
    public string? Issuer { get; set; }
    public string? Audience { get; set; }
}

public class GitHubAuthenticationOptions
{
    public string? ClientId { get; set; }
    public string? ClientSecret { get; set; }
}