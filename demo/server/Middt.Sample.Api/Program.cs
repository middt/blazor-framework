using Microsoft.EntityFrameworkCore;
using Middt.Framework.Api.DependencyInjection;
using Middt.Framework.Plugin.Rebus;
using Middt.Framework.Plugin.Redis;
using Middt.Framework.Ui.Server.DependencyInjection;
using Middt.Framework.Ui.Server.Endpoints;
using Middt.Framework.Workflow.DependencyInjection;
using Middt.Framework.Workflow.Endpoints;
using Middt.Framework.Workflow.Runtime;
using Middt.Framework.Workflow.Tasks;
using Middt.Sample.Api;
using Middt.Sample.Api.Handlers;
using Middt.Sample.Common.Messages;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();
builder.AddMiddtApi(options => options.Title = "Middt Sample API");

var signalR = builder.Services.AddSignalR();

// Optional infrastructure: enabled only when a connection string is configured (Aspire / Docker Compose injects them).
if (builder.Configuration.GetConnectionString("redis") is { Length: > 0 } redis)
{
    builder.Services.AddStackExchangeRedisCache(o => o.Configuration = redis); // L2 for HybridCache / IBaseCache / CacheTask
    signalR.AddStackExchangeRedis(redis);                                       // SignalR backplane
    builder.Services.AddMiddtRedisCache(o => o.ConnectionString = redis);       // distributed lock + cache plugin
}

// Messaging (Rebus): RabbitMQ when a connection string is configured, otherwise the in-memory transport for local runs.
var messaging = builder.Configuration.GetConnectionString("messaging");
builder.Services.AddMiddtRebus(
    settings =>
    {
        builder.Configuration.GetSection(RebusSettings.SectionName).Bind(settings);
        settings.ConnectionString = messaging;
        settings.UseInMemoryTransport = string.IsNullOrEmpty(messaging);
    },
    rebus => rebus.Subscribe<WorkflowNotification>());
builder.Services.AddMiddtRebusHandlersFromAssemblyOf<WorkflowNotificationHandler>();
builder.Services.AddHostedService<RabbitMqQueueInitializer>();

var dbConnectionString = builder.Configuration.GetConnectionString("WorkflowDb")
    ?? builder.Configuration.GetConnectionString("BikeStores");

// Workflow engine: JSON definitions from ./Workflows, all domain data stored in WorkflowInstanceData.Data JSONB ("wf" schema).
builder.Services.AddMiddtWorkflow(workflow =>
{
    workflow.UseDbContext(db => db.UseNpgsql(dbConnectionString));
    workflow.AutoCreateSchema = true;
    workflow.ExposeErrorDetails = builder.Environment.IsDevelopment();
    workflow.AddDefinitionsFromDirectory("Workflows");
    workflow.AddRebusTasks();
});
builder.Services.AddSingleton<IWorkflowMessagePublisher, RebusWorkflowMessagePublisher>();
builder.Services.AddHostedService<DemoWorkflowSeeder>();

// Server driven UI: JSON layouts (pages and workflow state views) in the "ui" schema, seeded from ./Layouts and editable at runtime.
builder.Services.AddMiddtUi(ui =>
{
    ui.UseDbContext(db => db.UseNpgsql(dbConnectionString));
    ui.AutoCreateSchema = true;
    ui.AddLayoutsFromDirectory("Layouts");
});
builder.Services.AddScoped<IWorkflowViewRenderer, WorkflowLayoutRenderer>();

var app = builder.Build();

app.UseMiddtApi();
app.MapHub<TestHub>("/hubs/testhub");
app.MapMiddtWorkflows("api/workflows");
app.MapMiddtUi("api/ui");

app.Run();

/// <summary>Entry point marker for <c>WebApplicationFactory</c> based integration tests.</summary>
public partial class Program;
