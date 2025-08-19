using Azure.Provisioning.AppService;

var builder = DistributedApplication.CreateBuilder(args);

builder.AddAzureAppServiceEnvironment("prod").ConfigureInfrastructure(infra =>
{
    var plan = infra.GetProvisionableResources()
        .OfType<AppServicePlan>()
        .Single();

    plan.Sku = new AppServiceSkuDescription
    {
        Name = "P0V3"
    };
});

var sql = builder.AddAzureSqlServer("sql")
    .RunAsContainer(o => o.WithLifetime(ContainerLifetime.Persistent));

var db = sql.AddDatabase("thediscdb");

var blobs = builder.AddAzureStorage("storage").RunAsEmulator(
                     azurite =>
                     {
                         azurite.WithLifetime(ContainerLifetime.Persistent);
                     })
    .AddBlobs("blobs");

var migrations = builder.AddProject<Projects.TheDiscDb_DatabaseMigration>("migrations")
    .WithReference(db)
    .WithReference(blobs)
    .WaitFor(blobs)
    .WaitFor(db);

var backend = builder.AddProject<Projects.TheDiscDb>("thediscdb-web")
    .WithExternalHttpEndpoints()
    .WithReference(db)
    .WithReference(blobs)
    .WithReference(migrations)
    .WaitForCompletion(migrations);

builder.Build().Run();
