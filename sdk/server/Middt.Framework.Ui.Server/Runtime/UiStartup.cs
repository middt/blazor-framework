using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Middt.Framework.Ui.Model;
using Middt.Framework.Ui.Server.Persistence;

namespace Middt.Framework.Ui.Server.Runtime;

/// <summary>Creates the layout tables (optional) and seeds layouts from the configured directories; invalid seeds fail the start.</summary>
internal sealed partial class UiStartup(IServiceScopeFactory scopeFactory, IHostEnvironment environment, IOptions<MiddtUiOptions> options, ILogger<UiStartup> logger) : IHostedService
{
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        // Parse and validate the seed files first: a broken file fails fast even when the database is unavailable.
        var seeds = LoadSeeds();

        await using var scope = scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<UiDbContext>();
        if (options.Value.AutoCreateSchema)
        {
            await EnsureTablesAsync(db, cancellationToken);
        }

        var store = scope.ServiceProvider.GetRequiredService<UiLayoutStore>();
        foreach (var (file, layout) in seeds)
        {
            if (await store.SeedAsync(layout, cancellationToken))
            {
                logger.LogInformation("Seeded layout {Key} v{Version} from {File}", layout.Key, layout.Version, Path.GetFileName(file));
            }
        }
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    private List<(string File, UiLayoutDocument Layout)> LoadSeeds()
    {
        var seeds = new List<(string, UiLayoutDocument)>();
        var keys = new HashSet<string>(StringComparer.Ordinal);
        using var scope = scopeFactory.CreateScope();
        var store = scope.ServiceProvider.GetRequiredService<UiLayoutStore>();

        foreach (var directory in options.Value.SeedDirectories)
        {
            var root = Path.IsPathRooted(directory) ? directory : Path.Combine(environment.ContentRootPath, directory);
            if (!Directory.Exists(root))
            {
                throw new DirectoryNotFoundException($"Layout directory '{root}' does not exist.");
            }

            foreach (var file in Directory.EnumerateFiles(root, "*.json", SearchOption.AllDirectories).Order(StringComparer.Ordinal))
            {
                UiLayoutDocument layout;
                try
                {
                    layout = UiJson.Parse(File.ReadAllText(file));
                }
                catch (System.Text.Json.JsonException ex)
                {
                    throw new InvalidOperationException($"Layout file '{file}' is not valid JSON: {ex.Message}", ex);
                }

                if (layout.Version == 0)
                {
                    layout.Version = 1;
                }

                var validation = store.Validate(layout);
                if (!validation.IsValid)
                {
                    throw new InvalidOperationException($"Layout file '{file}' is invalid:{Environment.NewLine}{string.Join(Environment.NewLine, validation.Errors)}");
                }

                if (!keys.Add(layout.Key))
                {
                    throw new InvalidOperationException($"Layout key '{layout.Key}' is defined more than once ({file}).");
                }

                seeds.Add((file, layout));
            }
        }

        return seeds;
    }

    private async Task EnsureTablesAsync(UiDbContext db, CancellationToken cancellationToken)
    {
        var creator = db.GetService<IRelationalDatabaseCreator>();
        if (!await creator.ExistsAsync(cancellationToken))
        {
            await creator.CreateAsync(cancellationToken);
        }

        var provider = db.Database.ProviderName ?? string.Empty;
        var sqlite = provider.Contains("Sqlite", StringComparison.Ordinal);
        bool exists;
        if (sqlite)
        {
            exists = await db.Database.SqlQuery<int>($"SELECT COUNT(*) AS \"Value\" FROM sqlite_master WHERE type = 'table' AND name = 'UiLayouts'").SingleAsync(cancellationToken) > 0;
        }
        else
        {
            var schema = options.Value.Schema ?? (provider.Contains("SqlServer", StringComparison.Ordinal) ? "dbo" : "public");
            exists = await db.Database.SqlQuery<int>($"SELECT COUNT(*) AS Value FROM information_schema.tables WHERE table_schema = {schema} AND table_name = 'UiLayouts'").SingleAsync(cancellationToken) > 0;
        }

        if (exists)
        {
            return;
        }

        if (!sqlite && options.Value.Schema is { Length: > 0 } name)
        {
            // Identifiers cannot be parameterised: only plain identifiers are accepted.
            if (!IdentifierRegex().IsMatch(name))
            {
                throw new InvalidOperationException($"Invalid UI schema name '{name}'.");
            }

#pragma warning disable EF1002 // name is a validated identifier
            if (provider.Contains("SqlServer", StringComparison.Ordinal))
            {
                await db.Database.ExecuteSqlRawAsync($"IF SCHEMA_ID(N'{name}') IS NULL EXEC('CREATE SCHEMA [{name}]')", cancellationToken);
            }
            else if (provider.Contains("Npgsql", StringComparison.Ordinal))
            {
                await db.Database.ExecuteSqlRawAsync($"CREATE SCHEMA IF NOT EXISTS \"{name}\"", cancellationToken);
            }
#pragma warning restore EF1002
        }

        await creator.CreateTablesAsync(cancellationToken);
    }

    [GeneratedRegex("^[A-Za-z_][A-Za-z0-9_]{0,63}$", RegexOptions.CultureInvariant)]
    private static partial Regex IdentifierRegex();
}
