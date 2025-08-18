var builder = DistributedApplication.CreateBuilder(args);

var sql = builder.AddSqlServer("sql")
                 .WithLifetime(ContainerLifetime.Persistent);

var db = sql.AddDatabase("thediscdb");

var migrations = builder.AddProject<Projects.TheDiscDb_DatabaseMigration>("migrations")
    .WithReference(db)
    .WaitFor(db);

var backend = builder.AddProject<Projects.TheDiscDb>("thediscdb-web")
    .WithReference(db)
    .WithReference(migrations)
    .WaitForCompletion(migrations);

builder.Build().Run();
