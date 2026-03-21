using System.Security.Cryptography;
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

var adminApiKey = ResolveAdminApiKey(builder);

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
    .WaitFor(db)
    .WithEnvironment("GraphQL__ApiKeyAuthentication__AdminApiKey", adminApiKey);

var backend = builder.AddProject<Projects.TheDiscDb>("thediscdb-web")
    .WithEndpoint("https", e => { e.Port = 7443; e.IsProxied = false; })
    .WithExternalHttpEndpoints()
    .WithReference(db)
    .WithReference(blobs)
    .WithReference(migrations)
    .WaitForCompletion(migrations)
    .WithChildRelationship(migrations)
    .WithEnvironment("GraphQL__ApiKeyAuthentication__ApiKey", adminApiKey);

builder.Build().Run();

static string ResolveAdminApiKey(IDistributedApplicationBuilder builder)
{
    var configured = builder.Configuration["GraphQL:ApiKeyAuthentication:AdminApiKey"];
    if (!string.IsNullOrEmpty(configured))
    {
        return configured;
    }

    var keyFilePath = Path.Combine(builder.AppHostDirectory, ".admin-apikey");
    if (File.Exists(keyFilePath))
    {
        var existing = File.ReadAllText(keyFilePath).Trim();
        if (!string.IsNullOrEmpty(existing))
        {
            return existing;
        }
    }

    var keyBytes = RandomNumberGenerator.GetBytes(32);
    var newKey = Convert.ToBase64String(keyBytes)
        .Replace("+", "-")
        .Replace("/", "_")
        .TrimEnd('=');

    File.WriteAllText(keyFilePath, newKey);
    return newKey;
}
