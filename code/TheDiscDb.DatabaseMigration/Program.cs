using Fantastic.FileSystem;
using Microsoft.EntityFrameworkCore;
using TheDiscDb.Data.Import;
using TheDiscDb.DatabaseMigration;
using TheDiscDb.Web.Data;

var builder = Host.CreateApplicationBuilder(args);

builder.AddServiceDefaults();
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

builder.Services.AddHttpClient();
builder.Services.AddSingleton<IFileSystem, PhysicalFileSystem>();
builder.Services.AddScoped<IDbContextFactory<SqlServerDataContext>, SingletonDbContextFactory>();
// todo replace with real one
builder.Services.AddSingleton<IStaticImageStore, NullStaticImageStore>();
builder.Services.Configure<DataImporterOptions>(o =>
{
    o.CleanImport = true;
});
builder.Services.AddScoped<DataImporter>();
builder.Services.Configure<DatabaseMigrationOptions>(builder.Configuration.GetSection("DatabaseMigration"));

var host = builder.Build();
host.Run();
