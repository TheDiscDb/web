using Syncfusion.Blazor;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using TheDiscDb.Client;
using KristofferStrube.Blazor.FileSystemAccess;
using TheDiscDb.Services;
using TheDiscDb.Services.Client;

var builder = WebAssemblyHostBuilder.CreateDefault(args);

Syncfusion.Licensing.SyncfusionLicenseProvider.RegisterLicense("Ngo9BigBOggjHTQxAR8/V1JFaF5cXGRCf1FpRmJGdld5fUVHYVZUTXxaS00DNHVRdkdmWH9eeHVURmdYVUZ0VkpWYEg=");
builder.Services.AddSyncfusionBlazor();

builder.Services.AddHttpClient();
builder.Services.AddScoped(sp => new HttpClient { BaseAddress = new Uri(builder.HostEnvironment.BaseAddress) });
builder.Services.AddScoped<ApiClient>();
builder.Services.AddScoped<TmdbClient>();
builder.Services.Configure<Fantastic.TheMovieDb.TheMovieDbOptions>(builder.Configuration.GetSection("TheMovieDb"));
builder.Services.AddSingleton<Fantastic.TheMovieDb.Caching.FileSystem.IFileSystemCache, NullFileSystemCache>();
builder.Services.AddScoped<Fantastic.TheMovieDb.TheMovieDbClient>();

builder.Services
    .AddTheDiscDbClient()
    .ConfigureHttpClient(client => client.BaseAddress = new Uri($"{builder.HostEnvironment.BaseAddress}graphql"));

builder.Services.AddFileSystemAccessService();
builder.Services.AddFileSystemAccessServiceInProcess();

builder.Services.AddScoped<IUserContributionService, UserContributionService>();

await builder.Build().RunAsync();
