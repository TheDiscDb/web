using System.Security.Cryptography;

var builder = DistributedApplication.CreateBuilder(args);

var adminApiKey = ResolveApiKey(builder, "AdminApiKey", ".admin-apikey");
var publicApiKey = ResolveApiKey(builder, "PublicApiKey", ".public-apikey");

var useExternalSql = string.Equals(builder.Configuration["UseExternalSql"], "true", StringComparison.OrdinalIgnoreCase);

var blobs = builder.AddAzureStorage("storage").RunAsEmulator(
                     azurite =>
                     {
                         azurite.WithLifetime(ContainerLifetime.Persistent);
                     })
    .AddBlobs("blobs");

var migrations = builder.AddProject<Projects.TheDiscDb_DatabaseMigration>("migrations")
    .WithReference(blobs)
    .WaitFor(blobs)
    .WithEnvironment("GraphQL__ApiKeyAuthentication__AdminApiKey", adminApiKey)
    .WithEnvironment("GraphQL__ApiKeyAuthentication__PublicApiKey", publicApiKey);

var backend = builder.AddProject<Projects.TheDiscDb>("thediscdb-web")
    .WithEndpoint("https", e => { e.Port = 7443; e.IsProxied = false; })
    .WithExternalHttpEndpoints()
    .WithReference(blobs)
    .WithReference(migrations)
    .WaitFor(migrations)
    .WithChildRelationship(migrations)
    .WithEnvironment("GraphQL__ApiKeyAuthentication__ApiKey", adminApiKey)
    .WithEnvironment("GraphQL__ApiKeyAuthentication__PublicApiKey", publicApiKey);

if (useExternalSql)
{
    var db = builder.AddConnectionString("thediscdb");
    migrations.WithReference(db);
    backend.WithReference(db);
}
else
{
    var sql = builder.AddAzureSqlServer("sql")
        .RunAsContainer(o => o.WithLifetime(ContainerLifetime.Persistent));
    var db = sql.AddDatabase("thediscdb");
    migrations.WithReference(db).WaitFor(db);
    backend.WithReference(db);
}

builder.Build().Run();

static string ResolveApiKey(IDistributedApplicationBuilder builder, string configKey, string fileName)
{
    var configured = builder.Configuration[$"GraphQL:ApiKeyAuthentication:{configKey}"];
    if (!string.IsNullOrEmpty(configured))
    {
        return configured;
    }

    var keyFilePath = Path.Combine(builder.AppHostDirectory, fileName);
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
