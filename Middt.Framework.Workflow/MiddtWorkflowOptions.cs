using System.Reflection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Middt.Framework.Workflow.Tasks;

namespace Middt.Framework.Workflow;

/// <summary>Configuration of <see cref="DependencyInjection.WorkflowServiceCollectionExtensions.AddMiddtWorkflow"/>.</summary>
public sealed class MiddtWorkflowOptions
{
    /// <summary>Database schema of the workflow tables (ignored by SQLite).</summary>
    public string? Schema { get; set; } = "wf";

    /// <summary>Creates the workflow tables at startup when they do not exist (use EF migrations for production).</summary>
    public bool AutoCreateSchema { get; set; }

    /// <summary>Runs the background processor for timers, timeouts and async transitions.</summary>
    public bool EnableJobProcessor { get; set; } = true;

    public TimeSpan JobPollingInterval { get; set; } = TimeSpan.FromSeconds(5);

    /// <summary>A Busy instance older than this is considered interrupted (crash) and marked Faulted.</summary>
    public TimeSpan BusyTimeout { get; set; } = TimeSpan.FromMinutes(5);

    /// <summary>Maximum automatic transitions executed in one chain (protects against loops).</summary>
    public int MaxAutomaticChainLength { get; set; } = 32;

    /// <summary>Maximum delay of in-request task retries; longer policies are capped.</summary>
    public TimeSpan MaxRetryDelay { get; set; } = TimeSpan.FromSeconds(30);

    internal Action<DbContextOptionsBuilder>? ConfigureDbContext { get; private set; }

    internal List<Func<IServiceProvider, IEnumerable<(string Source, string Json)>>> DefinitionSources { get; } = [];

    internal Dictionary<string, Type> TaskTypes { get; } = new(StringComparer.Ordinal);

    public MiddtWorkflowOptions UseDbContext(Action<DbContextOptionsBuilder> configure)
    {
        ConfigureDbContext = configure;
        return this;
    }

    /// <summary>Loads every <c>*.json</c> file of <paramref name="directory"/> (relative paths resolve against the content root).</summary>
    public MiddtWorkflowOptions AddDefinitionsFromDirectory(string directory)
    {
        DefinitionSources.Add(sp =>
        {
            var root = Path.IsPathRooted(directory)
                ? directory
                : Path.Combine(sp.GetService<Microsoft.Extensions.Hosting.IHostEnvironment>()?.ContentRootPath ?? AppContext.BaseDirectory, directory);

            return Directory.Exists(root)
                ? Directory.EnumerateFiles(root, "*.json", SearchOption.AllDirectories).Select(f => (f, File.ReadAllText(f)))
                : throw new DirectoryNotFoundException($"Workflow definition directory '{root}' does not exist.");
        });
        return this;
    }

    /// <summary>Loads embedded JSON resources whose name starts with <paramref name="resourcePrefix"/>.</summary>
    public MiddtWorkflowOptions AddDefinitionsFromAssembly(Assembly assembly, string resourcePrefix = "")
    {
        DefinitionSources.Add(_ => assembly.GetManifestResourceNames()
            .Where(n => n.StartsWith(resourcePrefix, StringComparison.Ordinal) && n.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
            .Select(n =>
            {
                using var stream = assembly.GetManifestResourceStream(n)!;
                using var reader = new StreamReader(stream);
                return (n, reader.ReadToEnd());
            }));
        return this;
    }

    public MiddtWorkflowOptions AddDefinition(string json, string source = "inline")
    {
        DefinitionSources.Add(_ => [(source, json)]);
        return this;
    }

    /// <summary>Registers an application task usable in definitions as <c>"task": "<paramref name="name"/>"</c>.</summary>
    public MiddtWorkflowOptions AddTask<TTask>(string name)
        where TTask : class, IWorkflowTask
    {
        TaskTypes[name] = typeof(TTask);
        return this;
    }
}
