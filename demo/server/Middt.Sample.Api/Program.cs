using Microsoft.EntityFrameworkCore;
using Middt.Framework.Api.DependencyInjection;
using Middt.Framework.Workflow.Razor;
using Middt.Framework.Plugin.Rebus;
using Middt.Framework.Workflow.DependencyInjection;
using Middt.Framework.Workflow.Endpoints;
using Middt.Framework.Workflow.Tasks;
using Middt.Framework.Plugin.Redis;
using Middt.Framework.Ui.Server.DependencyInjection;
using Middt.Framework.Ui.Server.Endpoints;
using Middt.Framework.Workflow.Runtime;
using Middt.Sample.Api;
using Middt.Sample.Api.Handlers;
using Middt.Sample.Api.Model.Database;
using Middt.Sample.Api.Repository.Bike;
using Middt.Sample.Common.Messages;
using Middt.Sample.Views;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();
builder.AddMiddtApi(options => options.Title = "Middt Sample API");

// Data access: one DbContext per request, repositories resolved by the controllers.
builder.Services.AddDbContextPool<BikeStoresContext>(options =>
    options.UseSqlServer(builder.Configuration.GetConnectionString("BikeStores")));
builder.Services.AddScoped<CategoryRepository>();
builder.Services.AddScoped<CustomerRepository>();
builder.Services.AddScoped<ProductRepository>();
builder.Services.AddScoped<StaffRepository>();
builder.Services.AddScoped<StoreRepository>();
builder.Services.AddScoped<ViewOrderRepository>();

var signalR = builder.Services.AddSignalR();

// Optional infrastructure: enabled only when a connection string is configured (Aspire injects them).
if (builder.Configuration.GetConnectionString("redis") is { Length: > 0 } redis)
{
    builder.Services.AddStackExchangeRedisCache(o => o.Configuration = redis); // L2 for HybridCache / IBaseCache
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
    rebus => rebus.Subscribe<CategoryCreated>().Subscribe<WorkflowNotification>());
builder.Services.AddMiddtRebusHandlersFromAssemblyOf<CategoryCreatedHandler>();

// Workflow engine: JSON definitions from ./Workflows, tables in the "wf" schema of the BikeStores database.
builder.Services.AddMiddtWorkflow(workflow =>
{
    workflow.UseDbContext(db => db.UseSqlServer(builder.Configuration.GetConnectionString("BikeStores")));
    workflow.AutoCreateSchema = true;
    // Raw task errors only in Development (production callers get a generic text; details stay in the logs).
    workflow.ExposeErrorDetails = builder.Environment.IsDevelopment();
    workflow.AddDefinitionsFromDirectory("Workflows");
    workflow.UseViews(WorkflowViewCatalog.FromAssemblyOf<OrderReviewView>().Views); // server driven UI (validated + HTML rendering)
});
builder.Services.AddSingleton<IWorkflowMessagePublisher, RebusWorkflowMessagePublisher>();

// Server driven UI: JSON layouts (pages and workflow state views) in the "ui" schema, seeded from ./Layouts and editable at runtime.
builder.Services.AddMiddtUi(ui =>
{
    ui.UseDbContext(db => db.UseSqlServer(builder.Configuration.GetConnectionString("BikeStores")));
    ui.AutoCreateSchema = true;
    ui.AddLayoutsFromDirectory("Layouts");
});
builder.Services.AddScoped<IWorkflowViewRenderer, WorkflowLayoutRenderer>(); // GET api/workflows/.../view for states with a layout

var app = builder.Build();

app.UseMiddtApi();
app.MapHub<TestHub>("/hubs/testhub");
app.MapMiddtWorkflows("api/workflows");
app.MapMiddtUi("api/ui");

app.Run();

/// <summary>Entry point marker for <c>WebApplicationFactory</c> based integration tests.</summary>
public partial class Program;
