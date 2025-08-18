
namespace TheDiscDb.Web.Controllers
{
    using System.IdentityModel.Tokens.Jwt;
    using System.Security.Claims;
    using System.Text;
    using System.Threading.Tasks;
    using HotChocolate.Authorization;
    using Microsoft.AspNetCore.Mvc;
    using Microsoft.Extensions.Options;
    using Microsoft.IdentityModel.Tokens;
    using TheDiscDb.Search;
    using TheDiscDb.Web.Authentication;

    [ApiController]
    [Route("api/admin")]
    public class AdminApiController : ControllerBase
    {
        private readonly IOptionsMonitor<JwtOptions> options;
        private readonly ISearchIndexService searchIndexService;

        public AdminApiController(IOptionsMonitor<JwtOptions> options, ISearchIndexService searchIndexService)
        {
            this.options = options ?? throw new ArgumentNullException(nameof(options));
            this.searchIndexService = searchIndexService ?? throw new ArgumentNullException(nameof(searchIndexService));
        }

        [HttpGet("authenticate")]
        public Task<AuthenticationToken> Authenticate(string user, string password)
        {
            if (this.options?.CurrentValue?.Key == null)
            {
                throw new Exception("Jwd:Key not configured");
            }

            var securityKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(this.options.CurrentValue.Key));
            var credentials = new SigningCredentials(securityKey, SecurityAlgorithms.HmacSha256);
            
            var claims = new[]
            {
                new Claim(ClaimTypes.NameIdentifier, user),
                new Claim(ClaimTypes.Role, "admin")
            };

            var token = new JwtSecurityToken(this.options.CurrentValue.Issuer,
                this.options.CurrentValue.Audience,
                claims,
                expires: DateTime.Now.AddMinutes(15), // TODO: From config
                signingCredentials: credentials);

            return Task.FromResult(new AuthenticationToken
            {
                Token = new JwtSecurityTokenHandler().WriteToken(token)
            });
        }

        [Authorize]
        [HttpGet("search/rebuild-index")]
        public async Task<BuildIndexSummary> RebuildSearchIndex()
        {
            var summary = await this.searchIndexService.BuildIndex();
            return summary;
        }

        [Authorize]
        [HttpPost("search/index-item")]
        public async Task<BuildIndexSummary> IndexItems(IEnumerable<SearchEntry> items)
        {
            var summary = await this.searchIndexService.IndexItems(items);
            return summary;
        }
    }
}
