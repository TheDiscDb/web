using System.Security.Cryptography;

var builder = DistributedApplication.CreateBuilder(args);

var adminApiKey = ResolveApiKey(builder, "AdminApiKey", ".admin-apikey");
var publicApiKey = ResolveApiKey(builder, "PublicApiKey", ".public-apikey");

// CJ (Commission Junction) Affiliate IDs for the GRUV program. Both are required to construct
// a CJ deep link of the form `https://www.anrdoezrs.net/click-{Pid}-{AdvertiserId}?url=...`.
// NOT secrets — the IDs are embedded in every outbound affiliate URL the site renders, like a
// `utm_source` value. They're the two integers in the `click-PID-AID` path of any CJ deep link
// for GRUV. The Pid here is the per-website "Promotional Property" identifier CJ assigns —
// NOT your CJ account login number. When either is missing, AffiliateLinkService degrades to
// emitting UTM-only URLs (no commission attribution); see AffiliateLinkService.Decorate.
//   Dev:  dotnet user-secrets --project TheDiscDb.AppHost set Gruv:Pid "<pid>"
//         dotnet user-secrets --project TheDiscDb.AppHost set Gruv:AdvertiserId "<aid>"
//   Prod: set environment variables Gruv__Pid and Gruv__AdvertiserId on the AppHost process.
var gruvPid = builder.Configuration["Gruv:Pid"] ?? "";
var gruvAdvertiserId = builder.Configuration["Gruv:AdvertiserId"] ?? "";

var useExternalSql = string.Equals(builder.Configuration["UseExternalSql"], "true", StringComparison.OrdinalIgnoreCase);
var useAzureFileShare = string.Equals(builder.Configuration["UseAzureFileShare"], "true", StringComparison.OrdinalIgnoreCase);

var blobs = builder.AddAzureStorage("storage").RunAsEmulator(
                     azurite =>
                     {
                         azurite.WithLifetime(ContainerLifetime.Persistent);
                     })
    .AddBlobs("blobs");

var dataDirectoryRoot = ResolveRequiredPath(builder, "DataDirectoryRoot");

var workspacePath = Path.Combine(dataDirectoryRoot, "import-workspace");

var migrations = builder.AddProject<Projects.TheDiscDb_DatabaseMigration>("migrations")
    .WithReference(blobs)
    .WaitFor(blobs)
    .WithEnvironment("DatabaseMigration__DataDirectoryRoot", dataDirectoryRoot)
    .WithEnvironment("ContributionImport__DataRepositoryPath", dataDirectoryRoot)
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
    .WithEnvironment("GraphQL__ApiKeyAuthentication__PublicApiKey", publicApiKey)
    .WithEnvironment("ContributionImport__DataRepositoryPath", dataDirectoryRoot)
    .WithEnvironment("ContributionImport__WorkspacePath", workspacePath);

// Forward Gruv affiliate IDs only when both are set on the AppHost. Aspire's
// .WithEnvironment overrides lower-precedence providers, so unconditionally forwarding empty
// strings would shadow any Gruv:* values the web project might have in its own user-secrets
// or appsettings (e.g., a dev who chose to configure them at the web tier). Atomic per pair:
// if either is missing here, forward neither and let the web project's own config win.
if (!string.IsNullOrWhiteSpace(gruvPid) && !string.IsNullOrWhiteSpace(gruvAdvertiserId))
{
    backend = backend
        .WithEnvironment("Gruv__Pid", gruvPid)
        .WithEnvironment("Gruv__AdvertiserId", gruvAdvertiserId);
}

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

static string ResolveRequiredPath(IDistributedApplicationBuilder builder, string key)
{
    var configuredPath = builder.Configuration[key];
    if (string.IsNullOrWhiteSpace(configuredPath))
    {
        throw new InvalidOperationException(
            $"Missing required AppHost configuration '{key}'. Set it in appsettings, environment variables, or user-secrets.");
    }

    if (Path.IsPathRooted(configuredPath))
    {
        return Path.GetFullPath(configuredPath);
    }

    return Path.GetFullPath(Path.Combine(builder.AppHostDirectory, configuredPath));
}

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
