using Microsoft.EntityFrameworkCore;

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


var host = builder.Build();
host.Run();
