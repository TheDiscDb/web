using System.Runtime.CompilerServices;
using System.Text;
using Azure;
using Azure.Storage.Blobs.Models;
using Blazorise;
using Blazorise.Bootstrap5;
using Blazorise.Icons.FontAwesome;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Azure;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using SixLabors.ImageSharp.Web.Caching.Azure;
using SixLabors.ImageSharp.Web.DependencyInjection;
using SixLabors.ImageSharp.Web.Providers.Azure;
using TheDiscDb;
using TheDiscDb.Data.GraphQL;
using TheDiscDb.Search;
using TheDiscDb.Web;
using TheDiscDb.Web.Authentication;
using TheDiscDb.Web.Data;
using TheDiscDb.Web.Sitemap;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();

builder.Services.AddControllersWithViews();
builder.Services.AddCors();
builder.Services.AddMemoryCache();

// Add services to the container.
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents()
    .AddInteractiveWebAssemblyComponents();

builder.Services
    .AddBlazorise(options =>
    {
        options.Immediate = true;
        options.ProductToken = "CjxRBHF6NA0+UAJxfDM1BlEAc3s1DD1WAHl+Nws7bjoNJ2ZdYhBVCCo/CTRQBUxERldhE1EvN0xcNm46FD1gSkUHCkxESVFvBl4yK1FBfAYKAiFoVXkNWTU3CDJTPHQAGkR/Xip0HhFIeVQ8bxMBUmtTPApwfjUIPG46HhFEbVgscw4DVXRJN3UeEUh5VDxvEwFSa1M8CnB+NQg8bjoeEUZwTTFkEhFadU07bx4cSm9fPG97fzUIAWlvHgJMa1g1eQQZWmdBImgeEVd3WzBvHnQ0CDxTAExEWmdYMXUEGEx9WzxvDA9dZ1MxfxYdWmc2UgBxfggyZyBKMnxwXmJSZDQrcVRaEHt4BzMXdiFqFAZjVjQOUnl7TAE4DWYyBUdIfTtmbi93Ym9UdG4dVk9YOmAtPWh2YjlfdH5gYiMPSjMlY1M+B2YCFzMXQRNiFiJPTnsAVTF6QEg0MAUoFzBdPyBdcD4wfnUreRsZc1x6L3EnHVxaPBVJGD88E0AuYQUpYm87UwUUBFd9eygFGSNDWmkhfTINTVAnUUhxfDxibTNDfA==\r\n\r\n";
    })
    .AddBootstrap5Providers()
    .AddFontAwesomeIcons();

builder.Services.AddPooledDbContextFactory<SqlServerDataContext>(options =>
{
    options.UseSqlServer(builder.Configuration.GetConnectionString("thediscdb"));
});
builder.EnrichSqlServerDbContext<SqlServerDataContext>();

builder.Services
.AddGraphQLServer()
    .ModifyCostOptions(o =>
    {
        o.EnforceCostLimits = false;
    })
    .AddFiltering()
    .AddSorting()
    .AddProjections()
    .RegisterDbContextFactory<SqlServerDataContext>()
    .AddQueryType<Query>();

builder.Services.AddHttpClient();
builder.Services.AddMemoryCache();
builder.Services.AddScoped<TheDiscDb.Client.SearchClient>();

var urls = Environment.GetEnvironmentVariable("ASPNETCORE_URLS")!.Split(";");
var serviceUrl = urls.FirstOrDefault(u => u.StartsWith("https"));

builder.Services
    .AddTheDiscDbClient()
    .ConfigureHttpClient(client => client.BaseAddress = new Uri($"{serviceUrl}/graphql"));

builder.Services.AddImageSharp()
    .ClearProviders()
    .Configure<AzureBlobStorageImageProviderOptions>(options =>
    {
        options.BlobContainers.Add(new AzureBlobContainerClientOptions
        {
            ConnectionString = builder.Configuration.GetValue<string>("BlobStorage:ConnectionString") ?? throw new Exception("Blob storage connection string not configured"),
            ContainerName = builder.Configuration.GetValue<string>("BlobStorage:Container") ?? throw new Exception("Blob storage container not configured")
        });
    })
    .Configure<AzureBlobStorageCacheOptions>(options =>
    {
        options.ConnectionString = builder.Configuration.GetValue<string>("BlobStorage:ConnectionString") ?? throw new ArgumentException("Required configuration 'BlobStorage:ConnectionString' was not found");
        options.ContainerName = "imagecache";

        // Optionally create the cache container on startup if not already created.
        AzureBlobStorageCache.CreateIfNotExists(options, PublicAccessType.None);
    })
    .SetCache<AzureBlobStorageCache>()
    .AddProvider<AzureBlobStorageImageProvider>();

builder.Services.AddAzureClients(b =>
{
    var endpoint = builder.Configuration["Search:Endpoint"];
    if (endpoint == null)
    {
        throw new Exception("Search:Endpoint not configured");
    }

    var apiKey = builder.Configuration["Search:ApiKey"];
    if (apiKey == null)
    {
        throw new Exception("Search:ApiKey not configured");
    }

    var uri = new Uri(endpoint);
    var credential = new AzureKeyCredential(apiKey);
    b.AddSearchIndexClient(uri, credential);
});
builder.Services.AddSingleton<ISearchService, SearchService>();
builder.Services.AddSingleton<ISearchIndexService, SearchIndexService>();
builder.Services.Configure<SearchOptions>(builder.Configuration.GetSection("Search"));
builder.Services.AddSingleton<CacheHelper>();
builder.Services.AddSingleton<SitemapGenerator>();

builder.Services.Configure<JwtOptions>(builder.Configuration.GetSection("Jwt"));
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme).AddJwtBearer(options => {

    var issuer = builder.Configuration["Jwt:Issuer"];
    if (issuer == null)
    {
        throw new Exception("Jwt:Issuer not configured");
    }

    var audience = builder.Configuration["Jwt:Audience"];
    if (audience == null)
    {
        throw new Exception("Jwt:Audience not configured");
    }

    var key = builder.Configuration["Jwt:Key"];
    if (key == null)
    {
        throw new Exception("Jwt:Key not configured");
    }

    options.TokenValidationParameters = new TokenValidationParameters
    {
        ValidateIssuer = true,
        ValidateAudience = true,
        ValidateLifetime = true,
        ValidateIssuerSigningKey = true,
        ValidIssuer = issuer,
        ValidAudience = audience,
        IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(key))
    };
});

var app = builder.Build();

app.MapDefaultEndpoints();

//app.MapDefaultEndpoints();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.UseWebAssemblyDebugging();
}
else
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseSitemap();

app.MapStaticAssets();
app.UseImageSharp();
app.UseMiddleware<RssFeedMidleware>();

app.MapGraphQL();

app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();
app.UseAntiforgery();

app.MapRazorComponents<TheDiscDb.Components.App>()
    .AddInteractiveServerRenderMode()
    .AddInteractiveWebAssemblyRenderMode()
    .AddAdditionalAssemblies(typeof(TheDiscDb.Client._Imports).Assembly);

app.Run();