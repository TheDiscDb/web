var builder = DistributedApplication.CreateBuilder(args);

var sql = builder.AddSqlServer("sql")
                 .WithLifetime(ContainerLifetime.Persistent);

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
    .WithReference(db)
    .WithReference(blobs)
    .WithReference(migrations)
    .WaitForCompletion(migrations);

builder.Build().Run();
