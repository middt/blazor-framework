// Local orchestration for the demo: PostgreSQL (middt_workflow + middt_identity), Redis, RabbitMQ, identity provider (:8083), API,
// Blazor UI and the Angular UI.
// Run with: dotnet run --project demo/Middt.Demo.AppHost  (requires Docker or Podman)
var builder = DistributedApplication.CreateBuilder(args);

var pgPassword = builder.AddParameter("postgres-password", secret: true);
var postgres = builder.AddPostgres("postgres", password: pgPassword)
    .WithDataVolume()
    .WithLifetime(ContainerLifetime.Persistent);

var workflowDb = postgres.AddDatabase("WorkflowDb", "middt_workflow");

var redis = builder.AddRedis("redis");
var messaging = builder.AddRabbitMQ("messaging").WithManagementPlugin();

var identityDb = postgres.AddDatabase("Identity", "middt_identity");
var identity = builder.AddProject<Projects.Middt_Identity>("middt-identity")
    .WithExternalHttpEndpoints()
    .WithEnvironment("Identity__ConfigFile", Path.GetFullPath(Path.Combine(builder.AppHostDirectory, "..", "identity.json")))
    .WithReference(identityDb).WaitFor(identityDb)
    .WithReference(redis).WaitFor(redis)
    .WithHttpHealthCheck("/health");

var api = builder.AddProject<Projects.Middt_Demo_Api>("middt-sample-api")
    .WithReference(workflowDb).WaitFor(workflowDb)
    .WithReference(redis).WaitFor(redis)
    .WithReference(messaging).WaitFor(messaging)
    .WaitFor(identity)
    .WithHttpHealthCheck("/health");

builder.AddProject<Projects.Middt_Demo_Blazor>("middt-sample-blazorserver")
    .WithExternalHttpEndpoints()
    .WithEnvironment("ApiSettings__URL", api.GetEndpoint("https"))
    .WithReference(redis)
    .WaitFor(api)
    .WaitFor(identity)
    .WithHttpHealthCheck("/health");

builder.AddProject<Projects.Middt_Demo_Angular>("middt-sample-angular")
    .WithExternalHttpEndpoints()
    .WithEnvironment("ApiSettings__URL", api.GetEndpoint("https"))
    .WithReference(redis)
    .WaitFor(api)
    .WaitFor(identity)
    .WithHttpHealthCheck("/health");

builder.Build().Run();
