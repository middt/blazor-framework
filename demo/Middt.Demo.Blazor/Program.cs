using Microsoft.AspNetCore.DataProtection;
using Middt.Framework.Blazor.DependencyInjection;
using Middt.Framework.Blazor.Ui;
using Middt.Framework.Client.Http;
using Middt.Demo.Blazor.Service;
using Middt.Demo.Blazor.SignalR;
using Middt.Framework.Client.DependencyInjection;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();
builder.AddMiddtBlazor();

// With Redis (Aspire / compose) sessions and Data Protection keys are shared and survive restarts.
if (builder.Configuration.GetConnectionString("redis") is { Length: > 0 } redis)
{
    builder.Services.AddStackExchangeRedisCache(o => o.Configuration = redis);
    builder.Services.AddDataProtection()
        .PersistKeysToStackExchangeRedis(StackExchange.Redis.ConnectionMultiplexer.Connect(redis), "middt-sample:dataprotection-keys");
}

// Typed API clients (IHttpClientFactory + standard resilience pipeline).
builder.Services.AddMiddtApiClient<WorkflowApiClient>();
builder.Services.AddMiddtUi(); // server driven UI: JSON layouts (/ui/{key}, workflow state layouts, /ui-admin editor)

builder.Services.AddScoped<MenuService>();
builder.Services.AddScoped<TestSignalRClient>();

var app = builder.Build();

app.UseMiddtBlazor();

app.Run();
