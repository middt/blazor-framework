using System.Reflection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Middt.Framework.Workflow.Tasks;

namespace Middt.Framework.Workflow;

/// <summary>Configuration of <see cref="DependencyInjection.WorkflowServiceCollectionExtensions.AddMiddtWorkflow"/>.</summary>
public sealed class MiddtWorkflowOptions
{
    /// <summary>Database schema of the workflow tables (ignored by SQLite).</summary>
    public string? Schema { get; set; } = "wf";

    /// <summary>Creates the workflow tables at startup when they do not exist (use EF migrations for production).</summary>
    public bool AutoCreateSchema { get; set; }

    /// <summary>Runs the background processor for timers, async transitions, follow-up jobs and Busy recovery.</summary>
    public bool EnableJobProcessor { get; set; } = true;

    public TimeSpan JobPollingInterval { get; set; } = TimeSpan.FromSeconds(5);

    /// <summary>A Busy instance older than this is considered interrupted (crash) and marked Faulted.</summary>
    public TimeSpan BusyTimeout { get; set; } = TimeSpan.FromMinutes(5);

    /// <summary>How often the job processor scans for instances stuck Busy longer than <see cref="BusyTimeout"/>.</summary>
    public TimeSpan BusyRecoveryInterval { get; set; } = TimeSpan.FromMinutes(1);

    /// <summary>Maximum automatic transitions executed in one chain (protects against loops).</summary>
    public int MaxAutomaticChainLength { get; set; } = 32;

    /// <summary>
    /// Returns the raw error text of faulted instances and incidents to API callers. Off by default: messages of failed
    /// tasks may reveal internal hosts, SQL or stack details; callers then see a generic text and operators read the
    /// details in the logs / database. Enable in Development.
    /// </summary>
    public bool ExposeErrorDetails { get; set; }

    /// <summary>
    /// Hosts the <c>http</c> task may call (exact host names, case-insensitive). Empty = any host of the definition's URL.
    /// Redirects are never followed, so a permitted host cannot bounce a request to an internal address.
    /// </summary>
    public ISet<string> AllowedHttpHosts { get; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

    /// <summary>Maximum delay of in-request task retries; longer policies are capped.</summary>
    public TimeSpan MaxRetryDelay { get; set; } = TimeSpan.FromSeconds(30);

    internal Action<DbContextOptionsBuilder>? ConfigureDbContext { get; private set; }

    internal List<Func<IServiceProvider, IEnumerable<(string Source, string Json)>>> DefinitionSources { get; } = [];

    internal Dictionary<string, Type> TaskTypes { get; } = new(StringComparer.OrdinalIgnoreCase);

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
                : Path.Combine(sp.GetService<IHostEnvironment>()?.ContentRootPath ?? AppContext.BaseDirectory, directory);

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
        where TTask : class, IWorkflowTask =>
        AddTask(name, typeof(TTask));

    /// <summary>Registers an application task type usable in definitions as <c>"task": "<paramref name="name"/>"</c>.</summary>
    public MiddtWorkflowOptions AddTask(string name, Type taskType)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(taskType);
        if (!typeof(IWorkflowTask).IsAssignableFrom(taskType) || taskType.IsAbstract || !taskType.IsClass)
        {
            throw new ArgumentException($"Task type '{taskType.FullName}' must be a non-abstract class implementing {nameof(IWorkflowTask)}.", nameof(taskType));
        }

        TaskTypes[name] = taskType;
        return this;
    }
}
