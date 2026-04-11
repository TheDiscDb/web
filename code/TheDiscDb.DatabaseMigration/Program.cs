using Fantastic.FileSystem;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using TheDiscDb.Data.Import;
using TheDiscDb.DatabaseMigration;
using TheDiscDb.Web.Data;

var builder = Host.CreateApplicationBuilder(args);

builder.AddServiceDefaults();

var seedingHealthCheck = new SeedingHealthCheck();
builder.Services.AddSingleton(seedingHealthCheck);
builder.Services.AddHealthChecks()
    .AddCheck("seeding", seedingHealthCheck);

builder.Services.AddHostedService<Worker>();

builder.Services.AddOpenTelemetry()
    .WithTracing(tracing => tracing.AddSource(Worker.ActivitySourceName));

builder.AddSqlServerDbContext<SqlServerDataContext>("thediscdb", configureDbContextOptions: options =>
{
    options.UseSqlServer(x =>
    {
        x.MigrationsAssembly("TheDiscDb.DatabaseMigration");
    });
});
builder.Services.AddScoped<IDbContextFactory<SqlServerDataContext>, SingletonDbContextFactory>();

builder.Services.AddIdentity<TheDiscDbUser, IdentityRole>()
    .AddEntityFrameworkStores<SqlServerDataContext>()
    .AddDefaultTokenProviders();

builder.AddAzureBlobServiceClient("blobs");
builder.Services.Configure<BlobStorageOptions>(builder.Configuration.GetSection("BlobStorage"));
builder.Services.AddSingleton<IStaticAssetStore, BlobStorageStaticAssetStore>();

builder.Services.AddHttpClient();
builder.Services.AddSingleton<IFileSystem, PhysicalFileSystem>();

builder.Services.Configure<DataImporterOptions>(builder.Configuration.GetSection("DataImporter"));
builder.Services.AddScoped<DataImporter>();
builder.Services.Configure<DatabaseMigrationOptions>(builder.Configuration.GetSection("DatabaseMigration"));
builder.Services.AddScoped<DataSeeder>();
builder.Services.AddScoped<BlobSyncService>();

var host = builder.Build();
host.Run();
