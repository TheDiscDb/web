using Fantastic.TheMovieDb.Caching.FileSystem;
using HighlightBlazor;
using KristofferStrube.Blazor.FileSystemAccess;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using Syncfusion.Blazor;
using Syncfusion.Blazor.Popups;
using TheDiscDb.Client;
using TheDiscDb.Client.Pages.Contribute;
using TheDiscDb.Client.Services;
using TheDiscDb.Services;
using TheDiscDb.Services.Client;

var builder = WebAssemblyHostBuilder.CreateDefault(args);

Syncfusion.Licensing.SyncfusionLicenseProvider.RegisterLicense("Ngo9BigBOggjHTQxAR8/V1JFaF5cXGRCf1FpRmJGdld5fUVHYVZUTXxaS00DNHVRdkdmWH9eeHVURmdYVUZ0VkpWYEg=");
builder.Services.AddSyncfusionBlazor();
builder.Services.AddScoped<SfDialogService>();

builder.Services.AddHttpClient();
builder.Services.AddScoped(sp => new HttpClient { BaseAddress = new Uri(builder.HostEnvironment.BaseAddress) });
builder.Services.AddScoped<ApiClient>();
builder.Services.Configure<Fantastic.TheMovieDb.TheMovieDbOptions>(builder.Configuration.GetSection("TheMovieDb"));
builder.Services.AddSingleton<IFileSystemCache, NullFileSystemCache>();
builder.Services.AddScoped<Fantastic.TheMovieDb.TheMovieDbClient>();
builder.Services.AddScoped<ExternalSearchDataAdaptor>();

builder.Services
    .AddTheDiscDbClient()
    .ConfigureHttpClient(client => client.BaseAddress = new Uri($"{builder.HostEnvironment.BaseAddress}graphql"));

builder.Services
    .AddContributionClient()
    .ConfigureHttpClient(client => client.BaseAddress = new Uri($"{builder.HostEnvironment.BaseAddress}graphql/contributions"));

builder.Services.AddFileSystemAccessService();
builder.Services.AddFileSystemAccessServiceInProcess();

builder.Services.AddScoped<IUserContributionService, UserContributionService>();
builder.Services.AddScoped<IExternalSearchService, ExternalSearchService>();

builder.Services.AddAuthorizationCore();
builder.Services.AddCascadingAuthenticationState();
builder.Services.AddAuthenticationStateDeserialization();
builder.Services.AddScoped<IClipboardService, ClipboardService>();
builder.Services.AddHighlight();
builder.Services.AddSingleton<IAmazonImporter, AmazonImporter>();

await builder.Build().RunAsync();
