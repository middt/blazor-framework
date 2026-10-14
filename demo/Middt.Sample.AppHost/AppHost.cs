// Local orchestration for the sample: SQL Server (+ BikeStores seed), Redis, RabbitMQ, API, Blazor UI and the Angular UI
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

var jwtSecret = builder.AddParameter("jwt-secret", secret: true);

var api = builder.AddProject<Projects.Middt_Sample_Api>("middt-sample-api")
    .WithReference(bikeStores).WaitFor(bikeStores)
    .WithReference(redis).WaitFor(redis)
    .WithReference(messaging).WaitFor(messaging)
    .WithEnvironment("JwtSettings__Secret", jwtSecret)
    .WithHttpHealthCheck("/health");

builder.AddProject<Projects.Middt_Sample_BlazorServer>("middt-sample-blazorserver")
    .WithExternalHttpEndpoints()
    .WithEnvironment("ApiSettings__URL", api.GetEndpoint("https"))
    .WithReference(redis)
    .WaitFor(api)
    .WithHttpHealthCheck("/health");

builder.AddProject<Projects.Middt_Sample_Angular>("middt-sample-angular")
    .WithExternalHttpEndpoints()
    .WithEnvironment("ApiSettings__URL", api.GetEndpoint("https"))
    .WithReference(redis)
    .WaitFor(api)
    .WithHttpHealthCheck("/health");

builder.Build().Run();
