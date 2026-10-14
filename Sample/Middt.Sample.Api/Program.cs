using Microsoft.EntityFrameworkCore;
using Middt.Framework.Api.DependencyInjection;
using Middt.Framework.Plugin.MassTransit;
using Middt.Framework.Plugin.Redis;
using Middt.Sample.Api;
using Middt.Sample.Api.Model.Database;
using Middt.Sample.Api.Repository.Bike;

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

if (builder.Configuration.GetConnectionString("messaging") is { Length: > 0 } messaging)
{
    builder.Services.AddMiddtMassTransit(o => o.ConnectionString = messaging);
}

var app = builder.Build();

app.UseMiddtApi();
app.MapHub<TestHub>("/hubs/testhub");

app.Run();

/// <summary>Entry point marker for <c>WebApplicationFactory</c> based integration tests.</summary>
public partial class Program;
