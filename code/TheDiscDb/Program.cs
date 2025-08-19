using Azure;
using Azure.Storage.Blobs.Models;
using Blazorise;
using Blazorise.Bootstrap5;
using Blazorise.Icons.FontAwesome;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Azure;
using SixLabors.ImageSharp.Web.Caching.Azure;
using SixLabors.ImageSharp.Web.DependencyInjection;
using SixLabors.ImageSharp.Web.Providers.Azure;
using TheDiscDb;
using TheDiscDb.Data.GraphQL;
using TheDiscDb.Search;
using TheDiscDb.Web;
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

var app = builder.Build();

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

app.Run();