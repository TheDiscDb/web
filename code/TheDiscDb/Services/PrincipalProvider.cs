using System.Security.Claims;

namespace TheDiscDb.Services.Server;

public interface IPrincipalProvider
{
    ClaimsPrincipal? Principal { get; }
}

public class PrincipalProvider : IPrincipalProvider
{
    private readonly IHttpContextAccessor httpContextAccessor;

    public PrincipalProvider(IHttpContextAccessor httpContextAccessor)
    {
        this.httpContextAccessor = httpContextAccessor ?? throw new ArgumentNullException(nameof(httpContextAccessor));
    }

    public ClaimsPrincipal? Principal => this.httpContextAccessor.HttpContext?.User;
}
