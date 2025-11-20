using Azure;
using Azure.Storage.Blobs.Models;
using Fantastic.TheMovieDb.Caching.FileSystem;
using HighlightBlazor;
using KristofferStrube.Blazor.FileSystemAccess;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Azure;
using Microsoft.Extensions.Options;
using SixLabors.ImageSharp.Web.Caching.Azure;
using SixLabors.ImageSharp.Web.DependencyInjection;
using SixLabors.ImageSharp.Web.Providers.Azure;
using Sqids;
using Syncfusion.Blazor;
using Syncfusion.Blazor.Popups;
using TheDiscDb;
using TheDiscDb.Client;
using TheDiscDb.Data.GraphQL;
using TheDiscDb.Data.Import;
using TheDiscDb.Search;
using TheDiscDb.Services;
using TheDiscDb.Services.Server;
using TheDiscDb.Web;
using TheDiscDb.Web.Data;
using TheDiscDb.Web.Sitemap;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();

builder.Services.AddTransient<ContributionEndpoints>();

builder.Services.AddControllersWithViews( options =>
{
    options.InputFormatters.Add(new PlainTextInputFormatter());
});
builder.Services.AddCors();
builder.Services.AddMemoryCache();

var gihubOptions = new TheDiscDb.Web.Authentication.AuthenticationOptions();
builder.Configuration.GetSection("Authentication:GitHub").Bind(gihubOptions);

var authBuilder = builder.Services.AddAuthentication();

// Only add github auth if configured
if (!string.IsNullOrEmpty(gihubOptions.ClientId) && !string.IsNullOrEmpty(gihubOptions.ClientSecret))
{
    authBuilder.AddGitHub(options =>
    {
        options.ClientId = gihubOptions.ClientId!;
        options.ClientSecret = gihubOptions.ClientSecret!;
        options.Scope.Add("read:user");
    });
}

var microsoftOptions = new TheDiscDb.Web.Authentication.AuthenticationOptions();
builder.Configuration.GetSection("Authentication:Microsoft").Bind(microsoftOptions);

if (!string.IsNullOrEmpty(microsoftOptions.ClientId) && !string.IsNullOrEmpty(microsoftOptions.ClientSecret))
{
    authBuilder.AddMicrosoftAccount(options =>
    {
        options.ClientId = microsoftOptions.ClientId;
        options.ClientSecret = microsoftOptions.ClientSecret;
    });
}

var googleOptions = new TheDiscDb.Web.Authentication.AuthenticationOptions();
builder.Configuration.GetSection("Authentication:Google").Bind(googleOptions);

if (!string.IsNullOrEmpty(googleOptions.ClientId) && !string.IsNullOrEmpty(googleOptions.ClientSecret))
{
    authBuilder.AddGoogle(options =>
    {
        options.ClientId = googleOptions.ClientId;
        options.ClientSecret = googleOptions.ClientSecret;
    });
}

builder.Services.AddIdentity<TheDiscDbUser, IdentityRole>()
    .AddEntityFrameworkStores<SqlServerDataContext>()
    .AddDefaultTokenProviders();

builder.Services.AddAuthorizationCore();
builder.Services.AddCascadingAuthenticationState();
builder.Services.AddAuthenticationStateDeserialization();
builder.Services.AddSingleton<IPrincipalProvider, PrincipalProvider>();

builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents()
    .AddInteractiveWebAssemblyComponents();

builder.Services.AddScoped<TheDiscDb.Components.Account.IdentityRedirectManager>();

Syncfusion.Licensing.SyncfusionLicenseProvider.RegisterLicense("Ngo9BigBOggjHTQxAR8/V1JFaF5cXGRCf1FpRmJGdld5fUVHYVZUTXxaS00DNHVRdkdmWH9eeHVURmdYVUZ0VkpWYEg=");
builder.Services.AddSyncfusionBlazor();
builder.Services.AddScoped<SfDialogService>();

builder.Services.AddPooledDbContextFactory<SqlServerDataContext>(options =>
{
    options.UseSqlServer(builder.Configuration.GetConnectionString("thediscdb"));
});
builder.EnrichSqlServerDbContext<SqlServerDataContext>();
builder.Services.AddScoped<SqlServerDataContext>(p => p.GetRequiredService<IDbContextFactory<SqlServerDataContext>>().CreateDbContext());

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
builder.Services.AddScoped<TheDiscDb.Client.ApiClient>();
builder.Services.AddScoped<IUserContributionService, TheDiscDb.Services.Server.UserContributionService>();
builder.Services.AddScoped<IExternalSearchService, TheDiscDb.Services.Server.ExternalSearchService>();
builder.Services.AddSingleton<IFileSystemCache, InMemoryFileSystemCache>();
builder.Services.Configure<Fantastic.TheMovieDb.TheMovieDbOptions>(builder.Configuration.GetSection("TheMovieDb"));
builder.Services.AddScoped<Fantastic.TheMovieDb.TheMovieDbClient>();
builder.Services.AddScoped<ExternalSearchDataAdaptor>();

var urls = Environment.GetEnvironmentVariable("ASPNETCORE_URLS")!.Split(";");
var serviceUrl = urls.FirstOrDefault(u => u.StartsWith("https"));

builder.Services
    .AddTheDiscDbClient()
    .ConfigureHttpClient(client => client.BaseAddress = new Uri($"{serviceUrl}/graphql"));

builder.AddAzureBlobServiceClient("blobs");
var blobConnectionString = builder.Configuration.GetConnectionString("blobs") ?? throw new Exception("Blob connection string not configured");
var blobContainerName = builder.Configuration.GetValue<string>("BlobStorage:Container") ?? throw new Exception("Blob storage container not configured");
builder.Services.AddImageSharp()
    .ClearProviders()
    .Configure<AzureBlobStorageImageProviderOptions>(options =>
    {
        options.BlobContainers.Add(new AzureBlobContainerClientOptions
        {
            ConnectionString = blobConnectionString,
            ContainerName = blobContainerName
        });
    })
    .Configure<AzureBlobStorageCacheOptions>(options =>
    {
        options.ContainerName = "imagecache";
        options.ConnectionString = blobConnectionString;

        // Optionally create the cache container on startup if not already created.
        AzureBlobStorageCache.CreateIfNotExists(options, PublicAccessType.None);
    })
    .SetCache<AzureBlobStorageCache>()
    .AddProvider<AzureBlobStorageImageProvider>();

builder.Services.AddSingleton<IOptions<BlobStorageOptions>>(provider =>
{
    return Options.Create(new BlobStorageOptions
    {
        ConnectionString = blobConnectionString,
        ContainerName = "contributions"
    });
});

builder.Services.AddSingleton<IStaticAssetStore, BlobStorageStaticAssetStore>();

var searchApiKey = builder.Configuration["Search:ApiKey"];
bool searchEnabled = !string.IsNullOrEmpty(searchApiKey);

builder.Services.AddAzureClients(b =>
{
    var endpoint = builder.Configuration["Search:Endpoint"];
    if (!searchEnabled || endpoint == null)
    {
        return;
    }

    var uri = new Uri(endpoint);
    var credential = new AzureKeyCredential(searchApiKey!);
    b.AddSearchIndexClient(uri, credential);
});

if (!searchEnabled)
{
    builder.Services.AddSingleton<ISearchService, NullSearchService>();
    builder.Services.AddSingleton<ISearchIndexService, NullSearchIndexService>();
}
else
{
    builder.Services.AddSingleton<ISearchService, SearchService>();
    builder.Services.AddSingleton<ISearchIndexService, SearchIndexService>();
}

builder.Services.Configure<SearchOptions>(builder.Configuration.GetSection("Search"));
builder.Services.AddSingleton<CacheHelper>();
builder.Services.AddSingleton<SitemapGenerator>();

builder.Services.AddFileSystemAccessService();
builder.Services.AddFileSystemAccessServiceInProcess();

builder.Services.AddSingleton<SqidsEncoder<int>>();
builder.Services.AddScoped<IClipboardService, ServerClipboardService>();
builder.Services.AddHighlight();

var app = builder.Build();

var contributionEndpoints = app.Services.GetRequiredService<ContributionEndpoints>();
contributionEndpoints.MapEndpoints(app);

app.MapDefaultEndpoints();

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

app.MapAdditionalIdentityEndpoints();

app.Run();