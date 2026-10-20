// Local orchestration for the demo: PostgreSQL (middt_workflow + middt_identity), Redis, RabbitMQ, OpenObserve (:5080),
// identity provider (:8083), API, Blazor UI and the Angular UI.
// Run with: dotnet run --project demo/Middt.Demo.AppHost  (requires Docker or Podman)
var builder = DistributedApplication.CreateBuilder(args);

var pgPassword = builder.AddParameter("postgres-password", secret: true);
var postgres = builder.AddPostgres("postgres", password: pgPassword)
    .WithDataVolume()
    .WithLifetime(ContainerLifetime.Persistent);

var workflowDb = postgres.AddDatabase("WorkflowDb", "middt_workflow");

var redis = builder.AddRedis("redis");
var messaging = builder.AddRabbitMQ("messaging").WithManagementPlugin();

const string openObserveUser = "admin@middt.local";
var openObservePassword = builder.AddParameter("openobserve-password", value: "Complexpass#123", secret: true);
var openObserve = builder.AddContainer("openobserve", "public.ecr.aws/zinclabs/openobserve", "latest")
    .WithHttpEndpoint(port: 5180, targetPort: 5080, name: "http")
    .WithEnvironment("ZO_ROOT_USER_EMAIL", openObserveUser)
    .WithEnvironment("ZO_ROOT_USER_PASSWORD", openObservePassword)
    .WithEnvironment("ZO_DATA_DIR", "/data")
    .WithVolume("middt-openobserve-data", "/data")
    .WithLifetime(ContainerLifetime.Persistent);
var openObserveEndpoint = openObserve.GetEndpoint("http");

var openObserveOtlpEndpoint = ReferenceExpression.Create($"{openObserveEndpoint}/api/default");
const string openObserveOtlpHeaders = "Authorization=Basic YWRtaW5AbWlkZHQubG9jYWw6Q29tcGxleHBhc3MjMTIz,stream-name=default";

var identityDb = postgres.AddDatabase("Identity", "middt_identity");
var identity = builder.AddProject<Projects.Middt_Identity>("middt-identity")
    .WithExternalHttpEndpoints()
    .WithEnvironment("Identity__ConfigFile", Path.GetFullPath(Path.Combine(builder.AppHostDirectory, "..", "identity.json")))
    .WithReference(identityDb).WaitFor(identityDb)
    .WithReference(redis).WaitFor(redis)
    .WithHttpHealthCheck("/health");

var api = builder.AddProject<Projects.Middt_Demo_Api>("middt-sample-api")
    .WithEnvironment("Telemetry__Otlp__Endpoint", openObserveOtlpEndpoint)
    .WithEnvironment("Telemetry__Otlp__Protocol", "http/protobuf")
    .WithEnvironment("Telemetry__Otlp__Headers", openObserveOtlpHeaders)
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
