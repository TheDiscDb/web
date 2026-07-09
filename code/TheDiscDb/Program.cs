using Azure;
using Azure.Storage.Blobs.Models;
using Fantastic.FileSystem;
using Fantastic.TheMovieDb.Caching.FileSystem;
using HighlightBlazor;
using KristofferStrube.Blazor.FileSystemAccess;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Azure;
using Microsoft.Extensions.Options;
using SixLabors.ImageSharp.Web.Caching.Azure;
using SixLabors.ImageSharp.Web.DependencyInjection;
using SixLabors.ImageSharp.Web.Middleware;
using SixLabors.ImageSharp.Web.Processors;
using SixLabors.ImageSharp.Web.Providers.Azure;
using Sqids;
using Syncfusion.Blazor;
using Syncfusion.Blazor.Popups;
using TheDiscDb;
using TheDiscDb.Client;
using TheDiscDb.Data.GraphQL;
using TheDiscDb.Data.Import;
using TheDiscDb.Data.Import.Pipeline;
using TheDiscDb.GraphQL;
using TheDiscDb.GraphQL.Contribute;
using TheDiscDb.GraphQL.Contribute.Mutations;
using TheDiscDb.Search;
using TheDiscDb.Services;
using TheDiscDb.Services.Admin;
using TheDiscDb.Services.Admin.GitHub;
using TheDiscDb.Services.Admin.Workspace;
using TheDiscDb.Services.EditSuggestions;
using TheDiscDb.Services.Server;
using TheDiscDb.Validation.Contribution;
using TheDiscDb.Validation.Boxset;
using TheDiscDb.Web;
using TheDiscDb.Web.Authentication;
using TheDiscDb.Web.Data;
using TheDiscDb.Web.Email;
using TheDiscDb.Web.Sitemap;
    
var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();

builder.Services.AddTransient<ContributionEndpoints>();
builder.Services.AddTransient<EngramEndpoints>();
builder.Services.AddTransient<DiscLookupEndpoints>();
builder.Services.AddEditSuggestions();

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
    .AddFiltering<DiscDbFilterConvention>()
    .AddSorting()
    .AddProjections()
    .RegisterDbContextFactory<SqlServerDataContext>()
    .DisableIntrospection(false)
    .TryAddTypeInterceptor<TitleItemProjectionTypeInterceptor>()
    .TryAddTypeInterceptor<ReleaseDiscProjectionTypeInterceptor>()
    .AddTypeExtension<TitleFileNameExtension>()
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
    .AddTypeExtension<UserContributionBoxsetTypeExtension>()
    .AddType<EncodedIdType>()
    .AddQueryType<ContributionQuery>()
    .AddMutationConventions(applyToAllMutations: true)
    .AddMutationType<ContributionMutations>()
    .AddTypeExtension<ApiKeyQueryExtension>()
    .AddTypeExtension<FileNameTemplateQueryExtension>();

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
    .Configure<ImageSharpMiddlewareOptions>(options =>
    {
        // Transparently serve WebP to browsers that advertise support for it. The source
        // blob (e.g. front.jpg) and request URL are unchanged: ImageSharp transcodes on the
        // fly and the browser honours the Content-Type, not the URL extension. Browsers that
        // do not send "Accept: image/webp" continue to receive the original format.
        options.OnParseCommandsAsync = context =>
        {
            if (context.Commands.Count > 0 && !context.Commands.Contains(FormatWebProcessor.Format))
            {
                // Honour RFC 7231 quality values: "image/webp;q=0" explicitly rejects WebP.
                bool acceptsWebp = context.Context.Request.GetTypedHeaders().Accept
                    .Any(static media =>
                        media.MediaType.Equals("image/webp", StringComparison.OrdinalIgnoreCase)
                        && media.Quality != 0);
                if (acceptsWebp)
                {
                    context.Commands[FormatWebProcessor.Format] = "webp";
                }
            }

            return Task.CompletedTask;
        };

        // The same URL can now yield different bytes depending on the Accept header, so
        // instruct shared caches (browser/CDN) to vary their stored response accordingly.
        options.OnPrepareResponseAsync = context =>
        {
            context.Response.Headers.Append("Vary", "Accept");
            return Task.CompletedTask;
        };
    })
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

// Gruv affiliate links — IGruvLinkLookup is scoped (per-request batched cache) so list pages
// rendering many releases issue a single DB query for all of them. AffiliateLinkService is
// stateless and singleton-safe (just reads IOptions). When Gruv:Pid / Gruv:AdvertiserId are not
// configured, the service degrades to plain UTM-tagged URLs (no CJ redirect, no commission).
builder.Services.Configure<TheDiscDb.Affiliate.AffiliateLinkOptions>(builder.Configuration.GetSection("Gruv"));
builder.Services.AddSingleton<TheDiscDb.Affiliate.AffiliateLinkService>();
builder.Services.AddScoped<TheDiscDb.Affiliate.IGruvLinkLookup, TheDiscDb.Affiliate.GruvLinkLookup>();

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

builder.Services.AddEditSuggestionNotifications(builder.Configuration);

builder.Services.AddSingleton<IContributionValidation, UniqueReleaseSlugValidation>();
builder.Services.AddSingleton<IContributionValidation, UniqueDiscSlugValidation>();
builder.Services.AddSingleton<IContributionValidation, ReleaseHasDiscsValidation>();
builder.Services.AddSingleton<IContributionValidation, ReleaseImageValidation>();

builder.Services.AddSingleton<IBoxsetValidation, BoxsetHasMembersValidation>();
builder.Services.AddSingleton<IBoxsetValidation, BoxsetHasImageValidation>();
builder.Services.AddSingleton<IBoxsetValidation, BoxsetSlugValidation>();
builder.Services.AddSingleton<IBoxsetValidation, BoxsetMemberDiscsValidation>();
builder.Services.AddSingleton<IBoxsetValidation, BoxsetMemberReleaseSlugValidation>();

builder.Services.AddSingleton<IFileSystem>(new PhysicalFileSystem());
builder.Services.Configure<DataImporterOptions>(builder.Configuration.GetSection("ContributionImport"));
builder.Services.AddImportPipeline(includeSearchIndex: false);

// Override the import pipeline middleware to upload images into the main images blob container
// (not the default "contributions" container) so that ImageSharp can serve them correctly.
builder.Services.AddSingleton<CoverImageUploadMiddleware>(provider =>
{
    var fileSystem = provider.GetRequiredService<IFileSystem>();
    var imageStore = provider.GetRequiredKeyedService<IStaticAssetStore>(KeyedServiceNames.ImagesAssetStore);
    return new CoverImageUploadMiddleware(fileSystem, imageStore);
});

builder.Services.AddSingleton<GroupImportMiddleware>(provider =>
{
    var dbFactory = provider.GetRequiredService<IDbContextFactory<SqlServerDataContext>>();
    var httpClient = provider.GetRequiredService<IHttpClientFactory>().CreateClient();
    var dataImportOptions = provider.GetRequiredService<IOptions<DataImporterOptions>>();
    var imageStore = provider.GetRequiredKeyedService<IStaticAssetStore>(KeyedServiceNames.ImagesAssetStore);
    var fileSystem = provider.GetRequiredService<IFileSystem>();
    return new GroupImportMiddleware(dbFactory, httpClient, dataImportOptions, imageStore, fileSystem);
});

builder.Services.Configure<ContributionImportOptions>(builder.Configuration.GetSection("ContributionImport"));
builder.Services.Configure<GitHubOptions>(builder.Configuration.GetSection("GitHub"));
builder.Services.AddSingleton<IDataRepositoryWorkspaceFactory, LocalDataRepositoryWorkspaceFactory>();
builder.Services.AddScoped<ContributionGeneratorService>();
builder.Services.AddScoped<ContributionImportPipelineRunner>();
builder.Services.AddScoped<GitHubPullRequestService>();
builder.Services.AddScoped<IContributionImportOrchestrator, ContributionImportOrchestrator>();

var app = builder.Build();

var contributionEndpoints = app.Services.GetRequiredService<ContributionEndpoints>();
contributionEndpoints.MapEndpoints(app);

var engramEndpoints = app.Services.GetRequiredService<EngramEndpoints>();
engramEndpoints.MapEndpoints(app);

var discLookupEndpoints = app.Services.GetRequiredService<DiscLookupEndpoints>();
discLookupEndpoints.MapEndpoints(app);

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

app.UseMiddleware<NotFoundFallbackMiddleware>();

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