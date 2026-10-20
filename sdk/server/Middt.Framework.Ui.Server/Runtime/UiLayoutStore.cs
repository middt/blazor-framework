using System.Diagnostics;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;
using Middt.Framework.Expressions;
using Middt.Framework.Ui.Model;
using Middt.Framework.Ui.Server.Persistence;
using Middt.Framework.Ui.Validation;

namespace Middt.Framework.Ui.Server.Runtime;

/// <summary>The layout failed validation.</summary>
public sealed class UiLayoutValidationException(IReadOnlyList<string> errors)
    : Exception($"Layout is invalid: {string.Join(" ", errors)}")
{
    public IReadOnlyList<string> Errors { get; } = errors;
}

/// <summary>The layout was changed by someone else (expected version differs).</summary>
public sealed class UiLayoutConflictException(string message) : Exception(message);

/// <summary>
/// Reads and writes layouts. Reads come from an in-memory snapshot of all layouts (refreshed after
/// <see cref="MiddtUiOptions.CacheDuration"/> or a local write); writes validate, keep a revision and use optimistic concurrency.
/// </summary>
public sealed class UiLayoutStore(UiDbContext db, IMemoryCache cache, UiLayoutValidator validator, TimeProvider timeProvider, IOptions<MiddtUiOptions> options)
{
    private static readonly ActivitySource ActivitySource = new("Middt.Framework.Api", "1.0.0");

    /// <summary>
    /// Singleton coordination of snapshot reloads: one reload at a time (no stampede on expiry) and a generation number so a
    /// reload that raced with a write does not cache the pre-write state.
    /// </summary>
    internal sealed class SnapshotGate
    {
        public SemaphoreSlim Reload { get; } = new(1, 1);

        public long Generation;
    }

    // One gate per cache instance (the store itself is scoped).
    private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<IMemoryCache, SnapshotGate> Gates = new();

    private SnapshotGate Gate => Gates.GetValue(cache, _ => new SnapshotGate());

    private const string CacheKey = "middt-ui:layouts";

    public const string SourceSeed = "seed";
    public const string SourceApi = "api";

    /// <summary>All layouts the user may open (layouts without roles are open to every authenticated user).</summary>
    public async Task<IReadOnlyList<UiLayoutSummary>> ListAsync(ExpressionUser user, string? kind = null, CancellationToken cancellationToken = default)
    {
        using var activity = ActivitySource.StartActivity("ui.layouts.list");
        if (!string.IsNullOrEmpty(kind))
        {
            activity?.SetTag("middt.ui.kind", kind);
        }

        var all = await SnapshotAsync(cancellationToken);
        return [.. all.Values
            .Where(e => kind is null || e.Document.Kind == kind)
            .Where(e => CanOpen(e.Document, user))
            .OrderBy(e => e.Document.Menu?.Order ?? int.MaxValue).ThenBy(e => e.Document.Key, StringComparer.Ordinal)
            .Select(e => e.Summary)];
    }

    public async Task<UiLayoutDocument?> FindAsync(string key, CancellationToken cancellationToken = default)
    {
        using var activity = ActivitySource.StartActivity($"ui.layouts.get {key}");
        activity?.SetTag("middt.ui.key", key);
        return (await SnapshotAsync(cancellationToken)).GetValueOrDefault(key)?.Document;
    }

    public bool CanOpen(UiLayoutDocument layout, ExpressionUser user) =>
        layout.Roles.Count == 0 || layout.Roles.Any(user.IsInRole) || user.IsInRole(options.Value.AdminRole);

    public UiValidationResult Validate(UiLayoutDocument layout)
    {
        using var activity = ActivitySource.StartActivity($"ui.layouts.validate {layout.Key}");
        activity?.SetTag("middt.ui.key", layout.Key);

        var result = validator.Validate(layout);
        if (UiJson.Serialize(layout).Length > options.Value.MaxLayoutLength)
        {
            result.Errors.Add($"Layout is larger than {options.Value.MaxLayoutLength} characters.");
        }

        activity?.SetTag("middt.ui.valid", result.IsValid);
        return result;
    }

    /// <summary>
    /// Creates or updates a layout. For updates <paramref name="expectedVersion"/> must equal the stored version
    /// (otherwise <see cref="UiLayoutConflictException"/>); the stored version moves to the revisions and the version increases.
    /// New layouts start at version 1.
    /// </summary>
    public async Task<UiLayoutDocument> SaveAsync(UiLayoutDocument layout, int? expectedVersion, string? user, CancellationToken cancellationToken = default)
    {
        using var activity = ActivitySource.StartActivity($"ui.layouts.save {layout.Key}");
        activity?.SetTag("middt.ui.key", layout.Key);

        var existing = await db.Layouts.SingleOrDefaultAsync(e => e.Key == layout.Key, cancellationToken);
        if (existing is not null && expectedVersion != existing.Version)
        {
            throw new UiLayoutConflictException($"Layout '{layout.Key}' was changed (version {existing.Version}); reload and apply your changes again.");
        }

        layout.Version = (existing?.Version ?? 0) + 1;
        await WriteAsync(existing, layout, SourceApi, user, cancellationToken);
        return layout;
    }

    /// <summary>Inserts a seed layout, or replaces the stored one when the seed has a higher version. Returns true when written.</summary>
    public async Task<bool> SeedAsync(UiLayoutDocument layout, CancellationToken cancellationToken = default)
    {
        var existing = await db.Layouts.SingleOrDefaultAsync(e => e.Key == layout.Key, cancellationToken);
        if (existing is not null && existing.Version >= layout.Version)
        {
            return false;
        }

        await WriteAsync(existing, layout, SourceSeed, "seed", cancellationToken);
        return true;
    }

    public async Task<bool> DeleteAsync(string key, int? expectedVersion, string? user, CancellationToken cancellationToken = default)
    {
        using var activity = ActivitySource.StartActivity($"ui.layouts.delete {key}");
        activity?.SetTag("middt.ui.key", key);

        var existing = await db.Layouts.SingleOrDefaultAsync(e => e.Key == key, cancellationToken);
        if (existing is null)
        {
            return false;
        }

        if (expectedVersion is { } version && version != existing.Version)
        {
            throw new UiLayoutConflictException($"Layout '{key}' was changed (version {existing.Version}).");
        }

        db.Revisions.Add(Revision(existing, user));
        db.Layouts.Remove(existing);
        await SaveChangesAsync(key, cancellationToken);
        return true;
    }

    public async Task<IReadOnlyList<UiLayoutRevisionModel>> RevisionsAsync(string key, CancellationToken cancellationToken = default)
    {
        using var activity = ActivitySource.StartActivity($"ui.layouts.revisions {key}");
        activity?.SetTag("middt.ui.key", key);

        var rows = await db.Revisions.AsNoTracking()
            .Where(r => r.Key == key)
            .OrderByDescending(r => r.Version)
            .Take(50)
            .ToListAsync(cancellationToken);
        activity?.SetTag("middt.ui.result_count", rows.Count);
        return [.. rows.Select(r => new UiLayoutRevisionModel { Version = r.Version, CreatedAt = r.CreatedAt, CreatedBy = r.CreatedBy, Content = r.Content })];
    }

    /// <summary>Drops the in-memory snapshot (e.g. after an external change).</summary>
    public void Invalidate()
    {
        Interlocked.Increment(ref Gate.Generation);
        cache.Remove(CacheKey);
    }

    private async Task WriteAsync(UiLayoutEntity? existing, UiLayoutDocument layout, string source, string? user, CancellationToken cancellationToken)
    {
        var validation = Validate(layout);
        if (!validation.IsValid)
        {
            throw new UiLayoutValidationException(validation.Errors);
        }

        var now = timeProvider.GetUtcNow();
        if (existing is null)
        {
            existing = new UiLayoutEntity { Key = layout.Key };
            db.Layouts.Add(existing);
        }
        else
        {
            db.Revisions.Add(Revision(existing, user));
        }

        existing.Kind = layout.Kind;
        existing.Title = layout.Title;
        existing.Content = UiJson.Serialize(layout);
        existing.Version = layout.Version;
        existing.Source = source;
        existing.UpdatedAt = now;
        existing.UpdatedBy = user;
        await SaveChangesAsync(layout.Key, cancellationToken);
    }

    private UiLayoutRevisionEntity Revision(UiLayoutEntity entity, string? user) => new()
    {
        Key = entity.Key,
        Version = entity.Version,
        Content = entity.Content,
        CreatedAt = timeProvider.GetUtcNow(),
        CreatedBy = user,
    };

    private async Task SaveChangesAsync(string key, CancellationToken cancellationToken)
    {
        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            throw new UiLayoutConflictException($"Layout '{key}' was changed concurrently; reload and try again.");
        }
        catch (DbUpdateException) when (db.ChangeTracker.Entries<UiLayoutEntity>().Any(e => e.State == EntityState.Added))
        {
            throw new UiLayoutConflictException($"Layout '{key}' was created concurrently; reload and try again.");
        }
        finally
        {
            Invalidate();
        }
    }

    private async Task<Dictionary<string, Entry>> SnapshotAsync(CancellationToken cancellationToken)
    {
        if (cache.TryGetValue(CacheKey, out Dictionary<string, Entry>? snapshot) && snapshot is not null)
        {
            Activity.Current?.SetTag("middt.ui.cache_hit", true);
            return snapshot;
        }

        var gate = Gate;
        await gate.Reload.WaitAsync(cancellationToken);
        try
        {
            // Another request may have loaded it while this one waited.
            if (cache.TryGetValue(CacheKey, out snapshot) && snapshot is not null)
            {
                Activity.Current?.SetTag("middt.ui.cache_hit", true);
                return snapshot;
            }

            Activity.Current?.SetTag("middt.ui.cache_hit", false);
            var generation = Interlocked.Read(ref gate.Generation);
            snapshot = await LoadSnapshotAsync(cancellationToken);
            if (Interlocked.Read(ref gate.Generation) == generation)
            {
                cache.Set(CacheKey, snapshot, new MemoryCacheEntryOptions
                {
                    AbsoluteExpirationRelativeToNow = options.Value.CacheDuration,
                    Size = 1, // required when the application's IMemoryCache has a SizeLimit
                });
            }

            return snapshot;
        }
        finally
        {
            gate.Reload.Release();
        }
    }

    private async Task<Dictionary<string, Entry>> LoadSnapshotAsync(CancellationToken cancellationToken)
    {
        using var activity = ActivitySource.StartActivity("ui.layouts.reload_snapshot");
        var rows = await db.Layouts.AsNoTracking().ToListAsync(cancellationToken);
        activity?.SetTag("middt.ui.layout_count", rows.Count);
        var result = new Dictionary<string, Entry>(StringComparer.Ordinal);
        foreach (var row in rows)
        {
            var document = UiJson.Parse(row.Content);
            document.Version = row.Version;
            result[row.Key] = new Entry(document, new UiLayoutSummary
            {
                Key = row.Key,
                Title = document.Title,
                Kind = document.Kind,
                Version = row.Version,
                Menu = document.Menu,
                UpdatedAt = row.UpdatedAt,
                UpdatedBy = row.UpdatedBy,
            });
        }

        return result;
    }

    private sealed record Entry(UiLayoutDocument Document, UiLayoutSummary Summary);
}
