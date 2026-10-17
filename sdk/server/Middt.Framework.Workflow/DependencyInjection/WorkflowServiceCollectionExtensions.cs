using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Middt.Framework.Workflow.Definitions;
using Middt.Framework.Workflow.Expressions;
using Middt.Framework.Workflow.Persistence;
using Middt.Framework.Workflow.Runtime;
using Middt.Framework.Workflow.Tasks;

namespace Middt.Framework.Workflow.DependencyInjection;

public static class WorkflowServiceCollectionExtensions
{
    /// <summary>
    /// Registers the workflow engine:
    /// <code>
    /// builder.Services.AddMiddtWorkflow(o => o
    ///     .UseDbContext(db => db.UseSqlServer(connectionString))
    ///     .AddDefinitionsFromDirectory("Workflows")
    ///     .AddTask&lt;NotifyCustomerTask&gt;("notify-customer"));
    /// app.MapMiddtWorkflows();
    /// </code>
    /// </summary>
    public static IServiceCollection AddMiddtWorkflow(this IServiceCollection services, Action<MiddtWorkflowOptions> configure)
    {
        ArgumentNullException.ThrowIfNull(configure);

        var options = new MiddtWorkflowOptions();
        configure(options);
        if (options.ConfigureDbContext is null)
        {
            throw new InvalidOperationException("Configure the workflow database with options.UseDbContext(...).");
        }

        services.Configure(configure);
        services.AddDbContext<WorkflowDbContext>(options.ConfigureDbContext);
        services.TryAddSingleton(TimeProvider.System);

        services.AddSingleton<WorkflowExpressionEngine>();
        services.AddSingleton<WorkflowTaskRegistry>();
        services.AddSingleton<WorkflowRegistry>();
        services.AddSingleton<WorkflowJobSignal>();
        services.AddScoped<WorkflowEngine>();
        services.AddScoped<WorkflowInstanceDataQuery>();
        services.AddScoped<WorkflowViewRenderer>();
        services.AddScoped<IWorkflowViewRenderer, ComponentWorkflowViewRenderer>();

        services.AddMemoryCache();
        services.TryAddTransient<SetTask>();
        services.TryAddTransient<LogTask>();
        services.TryAddTransient<HttpTask>();
        services.TryAddTransient<RabbitMqTask>();
        services.TryAddTransient<PublishTask>();
        services.TryAddTransient<StartWorkflowTask>();
        services.TryAddTransient<SetKeyTask>();
        services.TryAddTransient<LoadWorkflowTask>();
        services.TryAddTransient<FanOutTask>();
        services.TryAddTransient<CacheTask>();
        foreach (var taskType in options.TaskTypes.Values)
        {
            services.TryAddTransient(taskType);
        }

        // No automatic redirects: a 3xx from a permitted endpoint must not lead to an internal address (SSRF).
        services.AddHttpClient(HttpTask.DefaultClientName)
            .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler { AllowAutoRedirect = false });

        services.AddHostedService<WorkflowStartup>();
        if (options.EnableJobProcessor)
        {
            services.AddSingleton<WorkflowJobProcessor>();
            services.AddHostedService(sp => sp.GetRequiredService<WorkflowJobProcessor>());
        }

        return services;
    }
}
