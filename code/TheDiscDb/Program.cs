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
using TheDiscDb.GraphQL.Contribute;
using TheDiscDb.GraphQL.Contribute.Mutations;
using TheDiscDb.Search;
using TheDiscDb.Services;
using TheDiscDb.Services.Server;
using TheDiscDb.Validation.Contribution;
using TheDiscDb.Web;
using TheDiscDb.Web.Authentication;
using TheDiscDb.Web.Data;
using TheDiscDb.Web.Email;
using TheDiscDb.Web.Sitemap;
    
var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();

builder.Services.AddTransient<ContributionEndpoints>();
builder.Services.AddTransient<EngramEndpoints>();

builder.Services.AddControllersWithViews( options =>
{
    options.InputFormatters.Add(new PlainTextInputFormatter());
});
builder.Services.AddCors();
builder.Services.AddMemoryCache();
builder.Services.AddSingleton<TheDiscDb.Web.Authentication.ApiKeyManager>();

var gihubOptions = new TheDiscDb.Web.Authentication.AuthenticationOptions();
builder.Configuration.GetSection("Authentication:GitHub").Bind(gihubOptions);

var authBuilder = builder.Services.AddAuthentication();

// API key authentication for /graphql
var apiKeyConfig = builder.Configuration.GetSection(ApiKeyAuthenticationDefaults.ConfigSection);
var apiKeyAuthEnabled = apiKeyConfig.GetValue<bool>("Enabled");
string apiKey = apiKeyConfig.GetValue<string>("ApiKey") ?? string.Empty;

authBuilder.AddScheme<ApiKeyAuthenticationOptions, ApiKeyAuthenticationHandler>(
    ApiKeyAuthenticationDefaults.Scheme, options =>
    {
        options.IsEnabled = apiKeyAuthEnabled;
    });

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

builder.Services.AddQuickGridEntityFrameworkAdapter();

builder.Services.AddAuthorizationCore(b =>
{
    b.AddPolicy("Admin", policy => policy.RequireRole(DefaultRoles.Administrator));
    b.AddPolicy(ApiKeyAuthenticationDefaults.PolicyName, policy =>
        policy.AddAuthenticationSchemes(ApiKeyAuthenticationDefaults.Scheme, IdentityConstants.ApplicationScheme)
              .RequireAuthenticatedUser());
});
builder.Services.AddCascadingAuthenticationState();
builder.Services.AddSingleton<IPrincipalProvider, PrincipalProvider>();

builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents()
    .AddAuthenticationStateSerialization()
    .AddInteractiveWebAssemblyComponents();

builder.Services.AddScoped<TheDiscDb.Components.Account.IdentityRedirectManager>();

Syncfusion.Licensing.SyncfusionLicenseProvider.RegisterLicense("Ngo9BigBOggjHTQxAR8/V1JHaF5cWWdCf1FpRmJGdld5fUVHYVZUTXxaS00DNHVRdkdlWXtfcXVcRWdYVk13XUtWYEo=");
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
    .DisableIntrospection(false)
    .TryAddTypeInterceptor<TitleItemProjectionTypeInterceptor>()
    .AddQueryType<Query>();

builder.Services
    .AddGraphQLServer("ContributionSchema")
    .ModifyCostOptions(o =>
    {
        o.EnforceCostLimits = false;
    })
    .AddFiltering<EncodedIdFilterConvention>()
    .AddSorting()
    .AddProjections()
    .RegisterDbContextFactory<SqlServerDataContext>()
    .DisableIntrospection(false)
    .AddAuthorization()
    .AddTypeExtension<ContributionTypeExtension>()
    .AddTypeExtension<ContributionDiscTypeExtension>()
    .AddTypeExtension<ContributionDiscItemTypeExtension>()
    .AddTypeExtension<UserContributionAudioTrackTypeExtension>()
    .AddTypeExtension<UserContributionChapterTypeExtension>()
    .AddTypeExtension<UserContributionDiscHashItemTypeExtension>()
    .AddType<EncodedIdType>()
    .AddQueryType<ContributionQuery>()
    .AddMutationConventions(applyToAllMutations: true)
    .AddMutationType<ContributionMutations>()
    .AddTypeExtension<ApiKeyQueryExtension>();

builder.Services.AddHttpClient();
builder.Services.AddMemoryCache();
builder.Services.AddScoped<TheDiscDb.Client.ApiClient>();
builder.Services.AddScoped<IExternalSearchService, TheDiscDb.Services.Server.ExternalSearchService>();
builder.Services.AddSingleton<IFileSystemCache, InMemoryFileSystemCache>();
builder.Services.Configure<Fantastic.TheMovieDb.TheMovieDbOptions>(builder.Configuration.GetSection("TheMovieDb"));
builder.Services.AddScoped<Fantastic.TheMovieDb.TheMovieDbClient>();
builder.Services.AddScoped<ExternalSearchDataAdaptor>();

var aspnetcoreUrls = Environment.GetEnvironmentVariable("ASPNETCORE_URLS");
var serviceUrl = aspnetcoreUrls?.Split(";").FirstOrDefault(u => u.StartsWith("https"))
    ?? $"http://localhost:{Environment.GetEnvironmentVariable("PORT") ?? "8080"}";

builder.Services
    .AddTheDiscDbClient()
    .ConfigureHttpClient(client =>
    {
        client.BaseAddress = new Uri($"{serviceUrl}/graphql");
        if (apiKeyAuthEnabled && !string.IsNullOrEmpty(apiKey))
        {
            client.DefaultRequestHeaders.Authorization =
                new System.Net.Http.Headers.AuthenticationHeaderValue(ApiKeyAuthenticationDefaults.Scheme, apiKey);
        }
    });

builder.Services
    .AddContributionClient()
    .ConfigureHttpClient(client =>
    {
        client.BaseAddress = new Uri($"{serviceUrl}/graphql/contributions");
        if (apiKeyAuthEnabled && !string.IsNullOrEmpty(apiKey))
        {
            client.DefaultRequestHeaders.Authorization =
                new System.Net.Http.Headers.AuthenticationHeaderValue(ApiKeyAuthenticationDefaults.Scheme, apiKey);
        }
    });

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

builder.Services.AddKeyedSingleton<IStaticAssetStore>(KeyedServiceNames.ImagesAssetStore, (provider, _) =>
{
    var blobServiceClient = provider.GetRequiredService<Azure.Storage.Blobs.BlobServiceClient>();
    return new BlobStorageStaticAssetStore(blobServiceClient, Options.Create(new BlobStorageOptions
    {
        ConnectionString = blobConnectionString,
        ContainerName = blobContainerName
    }));
});

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
builder.Services.AddSingleton<IdEncoder>();
builder.Services.AddScoped<IClipboardService, ServerClipboardService>();
builder.Services.AddHighlight();
builder.Services.AddSingleton<IAmazonImporter, AmazonImporter>();
builder.Services.AddScoped<IContributionHistoryService, ContributionHistoryService>();
builder.Services.AddScoped<IMessageService, MessageService>();

// Mailgun email — optional, no-ops if ApiKey is not configured
builder.Services.Configure<MailgunOptions>(builder.Configuration.GetSection("Mailgun"));
var mailgunApiKey = builder.Configuration.GetValue<string>("Mailgun:ApiKey");
if (!string.IsNullOrEmpty(mailgunApiKey))
{
    builder.Services.AddMailgunClient();
    builder.Services.AddTransient<IContributionNotificationService, ContributionNotificationService>();
}
else
{
    builder.Services.AddTransient<IContributionNotificationService, NullContributionNotificationService>();
}

builder.Services.AddSingleton<IContributionValidation, UniqueReleaseSlugValidation>();
builder.Services.AddSingleton<IContributionValidation, UniqueDiscSlugValidation>();
builder.Services.AddSingleton<IContributionValidation, ReleaseHasDiscsValidation>();
builder.Services.AddSingleton<IContributionValidation, ReleaseImageValidation>();

var app = builder.Build();

var contributionEndpoints = app.Services.GetRequiredService<ContributionEndpoints>();
contributionEndpoints.MapEndpoints(app);

var engramEndpoints = app.Services.GetRequiredService<EngramEndpoints>();
engramEndpoints.MapEndpoints(app);

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

app.UseMiddleware<WasmConfigMiddleware>();
app.MapStaticAssets();
app.UseImageSharp();
app.UseMiddleware<RssFeedMidleware>();
app.UseMiddleware<LowercaseUrlMiddleware>();

app.UseAuthentication();
app.UseAuthorization();

app.UseWhen(
    ctx => ctx.Request.Path.StartsWithSegments("/graphql"),
    branch => branch.UseMiddleware<ApiKeyUsageMiddleware>());

var graphqlEndpoint = app.MapGraphQL();
if (apiKeyAuthEnabled)
{
    graphqlEndpoint.RequireAuthorization(ApiKeyAuthenticationDefaults.PolicyName);
}

app.MapGraphQL("/graphql/contributions", schemaName: "ContributionSchema")
   .RequireAuthorization(ApiKeyAuthenticationDefaults.PolicyName);

app.MapControllers();
app.UseAntiforgery();

app.MapRazorComponents<TheDiscDb.Components.App>()
    .AddInteractiveServerRenderMode()
    .AddInteractiveWebAssemblyRenderMode()
    .AddAdditionalAssemblies(typeof(TheDiscDb.Client._Imports).Assembly);

app.MapAdditionalIdentityEndpoints();

app.Run();