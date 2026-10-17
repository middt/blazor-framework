// Local orchestration for the sample: SQL Server (MiddtWorkflow + MiddtIdentity), Redis, RabbitMQ, identity provider (:8083), API,
// Blazor UI and the Angular UI.
// Run with: dotnet run --project demo/Middt.Sample.AppHost  (requires Docker or Podman)
var builder = DistributedApplication.CreateBuilder(args);

var sqlPassword = builder.AddParameter("sql-password", secret: true);
var sql = builder.AddSqlServer("sql", sqlPassword)
    .WithDataVolume()
    .WithLifetime(ContainerLifetime.Persistent);

var workflowDb = sql.AddDatabase("WorkflowDb", "MiddtWorkflow");

var redis = builder.AddRedis("redis");
var messaging = builder.AddRabbitMQ("messaging").WithManagementPlugin();

var identityDb = sql.AddDatabase("Identity", "MiddtIdentity");
var identity = builder.AddProject<Projects.Middt_Identity>("middt-identity")
    .WithExternalHttpEndpoints()
    .WithEnvironment("Identity__ConfigFile", Path.GetFullPath(Path.Combine(builder.AppHostDirectory, "..", "server", "identity.json")))
    .WithReference(identityDb).WaitFor(identityDb)
    .WithReference(redis).WaitFor(redis)
    .WithHttpHealthCheck("/health");

var api = builder.AddProject<Projects.Middt_Sample_Api>("middt-sample-api")
    .WithReference(workflowDb).WaitFor(workflowDb)
    .WithReference(redis).WaitFor(redis)
    .WithReference(messaging).WaitFor(messaging)
    .WaitFor(identity)
    .WithHttpHealthCheck("/health");

builder.AddProject<Projects.Middt_Sample_BlazorServer>("middt-sample-blazorserver")
    .WithExternalHttpEndpoints()
    .WithEnvironment("ApiSettings__URL", api.GetEndpoint("https"))
    .WithReference(redis)
    .WaitFor(api)
    .WaitFor(identity)
    .WithHttpHealthCheck("/health");

builder.AddProject<Projects.Middt_Sample_Angular>("middt-sample-angular")
    .WithExternalHttpEndpoints()
    .WithEnvironment("ApiSettings__URL", api.GetEndpoint("https"))
    .WithReference(redis)
    .WaitFor(api)
    .WaitFor(identity)
    .WithHttpHealthCheck("/health");

builder.Build().Run();
