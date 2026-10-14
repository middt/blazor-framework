// Local orchestration for the sample: SQL Server (+ BikeStores seed), Redis, RabbitMQ, identity provider (:8083), API,
// Blazor UI and the Angular UI
// (BFF host; build the SPA first: cd demo/client/angular/app && npm run build:sdk && npm ci && npx ng build sample).
// Run with: dotnet run --project demo/Middt.Sample.AppHost  (requires Docker or Podman)
var builder = DistributedApplication.CreateBuilder(args);

var sqlPassword = builder.AddParameter("sql-password", secret: true);
var sql = builder.AddSqlServer("sql", sqlPassword)
    .WithDataVolume()
    .WithLifetime(ContainerLifetime.Persistent);

// The seed script creates the BikeStores database itself, so it is mounted and executed by the container.
var bikeStores = sql.AddDatabase("BikeStores")
    .WithCreationScript(File.ReadAllText(Path.Combine(builder.AppHostDirectory, "..", "..", "data", "database.sql"), System.Text.Encoding.Unicode));

var redis = builder.AddRedis("redis");
var messaging = builder.AddRabbitMQ("messaging").WithManagementPlugin();

// Middt Identity (OAuth 2.0 / OpenID Connect server, identity/Middt.Identity) with the demo's settings
// (demo/server/identity.json: issuer :8083, clients middt-blazor / middt-angular, demo users). Client registrations use
// the fixed launch profile ports (Blazor https://localhost:7020, Angular :8081); the BFFs' Development client secrets
// match the demo file.
var identityDb = sql.AddDatabase("Identity", "MiddtIdentity");
var identity = builder.AddProject<Projects.Middt_Identity>("middt-identity")
    .WithExternalHttpEndpoints()
    .WithEnvironment("Identity__ConfigFile", Path.GetFullPath(Path.Combine(builder.AppHostDirectory, "..", "server", "identity.json")))
    .WithReference(identityDb).WaitFor(identityDb)
    .WithReference(redis).WaitFor(redis)
    .WithHttpHealthCheck("/health");

var api = builder.AddProject<Projects.Middt_Sample_Api>("middt-sample-api")
    .WithReference(bikeStores).WaitFor(bikeStores)
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
