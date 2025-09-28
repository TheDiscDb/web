using Blazorise;
using Blazorise.Bootstrap5;
using Blazorise.Icons.FontAwesome;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using TheDiscDb.Client;
using KristofferStrube.Blazor.FileSystemAccess;

var builder = WebAssemblyHostBuilder.CreateDefault(args);

builder.Services
    .AddBlazorise(options =>
    {
        options.Immediate = true;
        options.ProductToken = "CjxRBHF6NA0+UAJxfDM1BlEAc3s1DD1WAHl+Nws7bjoNJ2ZdYhBVCCo/CTRQBUxERldhE1EvN0xcNm46FD1gSkUHCkxESVFvBl4yK1FBfAYKAiFoVXkNWTU3CDJTPHQAGkR/Xip0HhFIeVQ8bxMBUmtTPApwfjUIPG46HhFEbVgscw4DVXRJN3UeEUh5VDxvEwFSa1M8CnB+NQg8bjoeEUZwTTFkEhFadU07bx4cSm9fPG97fzUIAWlvHgJMa1g1eQQZWmdBImgeEVd3WzBvHnQ0CDxTAExEWmdYMXUEGEx9WzxvDA9dZ1MxfxYdWmc2UgBxfggyZyBKMnxwXmJSZDQrcVRaEHt4BzMXdiFqFAZjVjQOUnl7TAE4DWYyBUdIfTtmbi93Ym9UdG4dVk9YOmAtPWh2YjlfdH5gYiMPSjMlY1M+B2YCFzMXQRNiFiJPTnsAVTF6QEg0MAUoFzBdPyBdcD4wfnUreRsZc1x6L3EnHVxaPBVJGD88E0AuYQUpYm87UwUUBFd9eygFGSNDWmkhfTINTVAnUUhxfDxibTNDfA==\r\n\r\n";
    })
    .AddBootstrap5Providers()
    .AddFontAwesomeIcons();

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

await builder.Build().RunAsync();
