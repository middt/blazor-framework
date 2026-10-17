using System.Collections.Immutable;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Middt.Framework.Model.Workflow;
using Middt.Framework.Workflow.Persistence;
using OpenIddict.Abstractions;
using static OpenIddict.Abstractions.OpenIddictConstants;

namespace Middt.Identity;

internal static class OpenIddictStoreJson
{
    public static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web);

    public static ImmutableDictionary<CultureInfo, string> ToCultureMap(Dictionary<string, string>? source)
    {
        if (source is null || source.Count == 0)
        {
            return ImmutableDictionary<CultureInfo, string>.Empty;
        }

        var builder = ImmutableDictionary.CreateBuilder<CultureInfo, string>();
        foreach (var (key, value) in source)
        {
            builder[CultureInfo.GetCultureInfo(key)] = value;
        }

        return builder.ToImmutable();
    }

    public static Dictionary<string, string> FromCultureMap(ImmutableDictionary<CultureInfo, string> map) =>
        map.ToDictionary(p => p.Key.Name, p => p.Value, StringComparer.Ordinal);

    public static ImmutableDictionary<string, JsonElement> ToPropertiesMap(Dictionary<string, JsonElement>? source) =>
        source is null || source.Count == 0
            ? ImmutableDictionary<string, JsonElement>.Empty
            : source.ToImmutableDictionary(StringComparer.Ordinal);

    public static Dictionary<string, JsonElement> FromPropertiesMap(ImmutableDictionary<string, JsonElement> map) =>
        map.ToDictionary(p => p.Key, p => p.Value, StringComparer.Ordinal);
}

/// <summary>OpenIddict application stored in <see cref="WorkflowDbContext.Instances"/> (<c>WorkflowKey = "oauth-application"</c>).</summary>
public sealed class WorkflowOpenIddictApplication
{
    public string Id { get; set; } = Guid.NewGuid().ToString("D");
    public string? ApplicationType { get; set; }
    public string? ClientId { get; set; }
    public string? ClientSecret { get; set; }
    public string? ClientType { get; set; }
    public string ConcurrencyToken { get; set; } = Guid.NewGuid().ToString("D");
    public string? ConsentType { get; set; }
    public string? DisplayName { get; set; }
    public Dictionary<string, string> DisplayNames { get; set; } = new(StringComparer.Ordinal);
    public string? JsonWebKeySetJson { get; set; }
    public List<string> Permissions { get; set; } = [];
    public List<string> PostLogoutRedirectUris { get; set; } = [];
    public Dictionary<string, JsonElement> Properties { get; set; } = new(StringComparer.Ordinal);
    public List<string> RedirectUris { get; set; } = [];
    public List<string> Requirements { get; set; } = [];
    public Dictionary<string, string> Settings { get; set; } = new(StringComparer.Ordinal);
}

/// <summary>OpenIddict scope stored in <see cref="WorkflowDbContext.Instances"/> (<c>WorkflowKey = "oauth-scope"</c>).</summary>
public sealed class WorkflowOpenIddictScope
{
    public string Id { get; set; } = Guid.NewGuid().ToString("D");
    public string ConcurrencyToken { get; set; } = Guid.NewGuid().ToString("D");
    public string? Description { get; set; }
    public Dictionary<string, string> Descriptions { get; set; } = new(StringComparer.Ordinal);
    public string? DisplayName { get; set; }
    public Dictionary<string, string> DisplayNames { get; set; } = new(StringComparer.Ordinal);
    public string? Name { get; set; }
    public Dictionary<string, JsonElement> Properties { get; set; } = new(StringComparer.Ordinal);
    public List<string> Resources { get; set; } = [];
}

/// <summary>OpenIddict authorization stored in <see cref="WorkflowDbContext.Instances"/> (<c>WorkflowKey = "oauth-authorization"</c>).</summary>
public sealed class WorkflowOpenIddictAuthorization
{
    public string Id { get; set; } = Guid.NewGuid().ToString("D");
    public string? ApplicationId { get; set; }
    public string ConcurrencyToken { get; set; } = Guid.NewGuid().ToString("D");
    public DateTimeOffset? CreationDate { get; set; }
    public Dictionary<string, JsonElement> Properties { get; set; } = new(StringComparer.Ordinal);
    public List<string> Scopes { get; set; } = [];
    public string? Status { get; set; }
    public string? Subject { get; set; }
    public string? Type { get; set; }
}

/// <summary>OpenIddict token stored in <see cref="WorkflowDbContext.Instances"/> (<c>WorkflowKey = "oauth-token"</c>).</summary>
public sealed class WorkflowOpenIddictToken
{
    public string Id { get; set; } = Guid.NewGuid().ToString("D");
    public string? ApplicationId { get; set; }
    public string? AuthorizationId { get; set; }
    public string ConcurrencyToken { get; set; } = Guid.NewGuid().ToString("D");
    public DateTimeOffset? CreationDate { get; set; }
    public DateTimeOffset? ExpirationDate { get; set; }
    public string? Payload { get; set; }
    public Dictionary<string, JsonElement> Properties { get; set; } = new(StringComparer.Ordinal);
    public DateTimeOffset? RedemptionDate { get; set; }
    public string? ReferenceId { get; set; }
    public string? Status { get; set; }
    public string? Subject { get; set; }
    public string? Type { get; set; }
}

internal sealed class WorkflowOpenIddictApplicationStore(WorkflowDbContext db) : IOpenIddictApplicationStore<WorkflowOpenIddictApplication>
{
    internal const string WorkflowKey = "oauth-application";

    public async ValueTask<long> CountAsync(CancellationToken cancellationToken) =>
        await db.Instances.AsNoTracking().LongCountAsync(x => x.WorkflowKey == WorkflowKey, cancellationToken);

    public async ValueTask<long> CountAsync<TResult>(Func<IQueryable<WorkflowOpenIddictApplication>, IQueryable<TResult>> query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        var all = await LoadAllAsync(cancellationToken);
        return query(all.AsQueryable()).LongCount();
    }

    public async ValueTask CreateAsync(WorkflowOpenIddictApplication application, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(application);
        var id = Guid.TryParse(application.Id, out var parsed) && parsed != Guid.Empty ? parsed : db.NewId();
        application.Id = id.ToString("D");
        application.ConcurrencyToken = Guid.NewGuid().ToString("D");
        var now = DateTime.UtcNow;

        db.Instances.Add(new WorkflowInstance
        {
            Id = id,
            WorkflowKey = WorkflowKey,
            WorkflowVersion = 1,
            BusinessKey = application.ClientId ?? application.Id,
            State = application.ClientType ?? ClientTypes.Confidential,
            Status = WorkflowInstanceStatus.Active,
            Data = JsonSerializer.Serialize(application, OpenIddictStoreJson.Options),
            Revision = 1,
            CreatedAt = now,
            UpdatedAt = now,
        });

        await db.SaveChangesAsync(cancellationToken);
    }

    public async ValueTask DeleteAsync(WorkflowOpenIddictApplication application, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(application);
        var instance = await FindInstanceAsync(application.Id, cancellationToken);
        if (instance is not null)
        {
            db.Instances.Remove(instance);
        }

        // Cascade delete authorizations and tokens belonging to this application.
        var authorizations = await db.Instances
            .Where(x => x.WorkflowKey == WorkflowOpenIddictAuthorizationStore.WorkflowKey && x.PendingTransition == application.Id)
            .ToListAsync(cancellationToken);
        db.Instances.RemoveRange(authorizations);

        var tokens = await db.Instances
            .Where(x => x.WorkflowKey == WorkflowOpenIddictTokenStore.WorkflowKey)
            .ToListAsync(cancellationToken);
        foreach (var tokenInstance in tokens)
        {
            if (JsonSerializer.Deserialize<WorkflowOpenIddictToken>(tokenInstance.Data, OpenIddictStoreJson.Options)?.ApplicationId == application.Id)
            {
                db.Instances.Remove(tokenInstance);
            }
        }

        await db.SaveChangesAsync(cancellationToken);
    }

    public async ValueTask<WorkflowOpenIddictApplication?> FindByClientIdAsync(string identifier, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrEmpty(identifier);
        var instance = await db.Instances.AsNoTracking()
            .FirstOrDefaultAsync(x => x.WorkflowKey == WorkflowKey && x.BusinessKey == identifier, cancellationToken);
        if (instance is not null)
        {
            return Deserialize(instance);
        }

        var all = await LoadAllAsync(cancellationToken);
        return all.FirstOrDefault(a => string.Equals(a.ClientId, identifier, StringComparison.Ordinal));
    }

    public async ValueTask<WorkflowOpenIddictApplication?> FindByIdAsync(string identifier, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrEmpty(identifier);
        var instance = await FindInstanceAsync(identifier, cancellationToken);
        return instance is null ? null : Deserialize(instance);
    }

    public async IAsyncEnumerable<WorkflowOpenIddictApplication> FindByPostLogoutRedirectUriAsync(
        string uri,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrEmpty(uri);
        foreach (var app in await LoadAllAsync(cancellationToken))
        {
            if (app.PostLogoutRedirectUris.Contains(uri, StringComparer.Ordinal))
            {
                yield return app;
            }
        }
    }

    public async IAsyncEnumerable<WorkflowOpenIddictApplication> FindByRedirectUriAsync(
        string uri,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrEmpty(uri);
        foreach (var app in await LoadAllAsync(cancellationToken))
        {
            if (app.RedirectUris.Contains(uri, StringComparer.Ordinal))
            {
                yield return app;
            }
        }
    }

    public ValueTask<string?> GetApplicationTypeAsync(WorkflowOpenIddictApplication application, CancellationToken cancellationToken) =>
        new(application.ApplicationType);

    public async ValueTask<TResult?> GetAsync<TState, TResult>(
        Func<IQueryable<WorkflowOpenIddictApplication>, TState, IQueryable<TResult>> query,
        TState state,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        var all = await LoadAllAsync(cancellationToken);
        return query(all.AsQueryable(), state).FirstOrDefault();
    }

    public ValueTask<string?> GetClientIdAsync(WorkflowOpenIddictApplication application, CancellationToken cancellationToken) =>
        new(application.ClientId);

    public ValueTask<string?> GetClientSecretAsync(WorkflowOpenIddictApplication application, CancellationToken cancellationToken) =>
        new(application.ClientSecret);

    public ValueTask<string?> GetClientTypeAsync(WorkflowOpenIddictApplication application, CancellationToken cancellationToken) =>
        new(application.ClientType);

    public ValueTask<string?> GetConsentTypeAsync(WorkflowOpenIddictApplication application, CancellationToken cancellationToken) =>
        new(application.ConsentType);

    public ValueTask<string?> GetDisplayNameAsync(WorkflowOpenIddictApplication application, CancellationToken cancellationToken) =>
        new(application.DisplayName);

    public ValueTask<ImmutableDictionary<CultureInfo, string>> GetDisplayNamesAsync(WorkflowOpenIddictApplication application, CancellationToken cancellationToken) =>
        new(OpenIddictStoreJson.ToCultureMap(application.DisplayNames));

    public ValueTask<string?> GetIdAsync(WorkflowOpenIddictApplication application, CancellationToken cancellationToken) =>
        new(application.Id);

    public ValueTask<JsonWebKeySet?> GetJsonWebKeySetAsync(WorkflowOpenIddictApplication application, CancellationToken cancellationToken) =>
        new(string.IsNullOrEmpty(application.JsonWebKeySetJson) ? null : JsonWebKeySet.Create(application.JsonWebKeySetJson));

    public ValueTask<ImmutableArray<string>> GetPermissionsAsync(WorkflowOpenIddictApplication application, CancellationToken cancellationToken) =>
        new([.. application.Permissions]);

    public ValueTask<ImmutableArray<string>> GetPostLogoutRedirectUrisAsync(WorkflowOpenIddictApplication application, CancellationToken cancellationToken) =>
        new([.. application.PostLogoutRedirectUris]);

    public ValueTask<ImmutableDictionary<string, JsonElement>> GetPropertiesAsync(WorkflowOpenIddictApplication application, CancellationToken cancellationToken) =>
        new(OpenIddictStoreJson.ToPropertiesMap(application.Properties));

    public ValueTask<ImmutableArray<string>> GetRedirectUrisAsync(WorkflowOpenIddictApplication application, CancellationToken cancellationToken) =>
        new([.. application.RedirectUris]);

    public ValueTask<ImmutableArray<string>> GetRequirementsAsync(WorkflowOpenIddictApplication application, CancellationToken cancellationToken) =>
        new([.. application.Requirements]);

    public ValueTask<ImmutableDictionary<string, string>> GetSettingsAsync(WorkflowOpenIddictApplication application, CancellationToken cancellationToken) =>
        new(application.Settings.ToImmutableDictionary(StringComparer.Ordinal));

    public ValueTask<WorkflowOpenIddictApplication> InstantiateAsync(CancellationToken cancellationToken) =>
        new(new WorkflowOpenIddictApplication { Id = db.NewId().ToString("D") });

    public async IAsyncEnumerable<WorkflowOpenIddictApplication> ListAsync(
        int? count,
        int? offset,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        IQueryable<WorkflowInstance> query = db.Instances.AsNoTracking()
            .Where(x => x.WorkflowKey == WorkflowKey)
            .OrderBy(x => x.BusinessKey);
        if (offset is { } skip)
        {
            query = query.Skip(skip);
        }

        if (count is { } take)
        {
            query = query.Take(take);
        }

        foreach (var instance in await query.ToListAsync(cancellationToken))
        {
            yield return Deserialize(instance);
        }
    }

    public async IAsyncEnumerable<TResult> ListAsync<TState, TResult>(
        Func<IQueryable<WorkflowOpenIddictApplication>, TState, IQueryable<TResult>> query,
        TState state,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        var all = await LoadAllAsync(cancellationToken);
        foreach (var item in query(all.AsQueryable(), state))
        {
            yield return item;
        }
    }

    public ValueTask SetApplicationTypeAsync(WorkflowOpenIddictApplication application, string? type, CancellationToken cancellationToken)
    {
        application.ApplicationType = type;
        return default;
    }

    public ValueTask SetClientIdAsync(WorkflowOpenIddictApplication application, string? identifier, CancellationToken cancellationToken)
    {
        application.ClientId = identifier;
        return default;
    }

    public ValueTask SetClientSecretAsync(WorkflowOpenIddictApplication application, string? secret, CancellationToken cancellationToken)
    {
        application.ClientSecret = secret;
        return default;
    }

    public ValueTask SetClientTypeAsync(WorkflowOpenIddictApplication application, string? type, CancellationToken cancellationToken)
    {
        application.ClientType = type;
        return default;
    }

    public ValueTask SetConsentTypeAsync(WorkflowOpenIddictApplication application, string? type, CancellationToken cancellationToken)
    {
        application.ConsentType = type;
        return default;
    }

    public ValueTask SetDisplayNameAsync(WorkflowOpenIddictApplication application, string? name, CancellationToken cancellationToken)
    {
        application.DisplayName = name;
        return default;
    }

    public ValueTask SetDisplayNamesAsync(WorkflowOpenIddictApplication application, ImmutableDictionary<CultureInfo, string> names, CancellationToken cancellationToken)
    {
        application.DisplayNames = OpenIddictStoreJson.FromCultureMap(names);
        return default;
    }

    public ValueTask SetJsonWebKeySetAsync(WorkflowOpenIddictApplication application, JsonWebKeySet? set, CancellationToken cancellationToken)
    {
        application.JsonWebKeySetJson = set is null ? null : JsonSerializer.Serialize(set, OpenIddictStoreJson.Options);
        return default;
    }

    public ValueTask SetPermissionsAsync(WorkflowOpenIddictApplication application, ImmutableArray<string> permissions, CancellationToken cancellationToken)
    {
        application.Permissions = [.. permissions];
        return default;
    }

    public ValueTask SetPostLogoutRedirectUrisAsync(WorkflowOpenIddictApplication application, ImmutableArray<string> uris, CancellationToken cancellationToken)
    {
        application.PostLogoutRedirectUris = [.. uris];
        return default;
    }

    public ValueTask SetPropertiesAsync(WorkflowOpenIddictApplication application, ImmutableDictionary<string, JsonElement> properties, CancellationToken cancellationToken)
    {
        application.Properties = OpenIddictStoreJson.FromPropertiesMap(properties);
        return default;
    }

    public ValueTask SetRedirectUrisAsync(WorkflowOpenIddictApplication application, ImmutableArray<string> uris, CancellationToken cancellationToken)
    {
        application.RedirectUris = [.. uris];
        return default;
    }

    public ValueTask SetRequirementsAsync(WorkflowOpenIddictApplication application, ImmutableArray<string> requirements, CancellationToken cancellationToken)
    {
        application.Requirements = [.. requirements];
        return default;
    }

    public ValueTask SetSettingsAsync(WorkflowOpenIddictApplication application, ImmutableDictionary<string, string> settings, CancellationToken cancellationToken)
    {
        application.Settings = settings.ToDictionary(p => p.Key, p => p.Value, StringComparer.Ordinal);
        return default;
    }

    public async ValueTask UpdateAsync(WorkflowOpenIddictApplication application, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(application);
        var instance = await FindInstanceAsync(application.Id, cancellationToken)
            ?? throw new OpenIddictExceptions.ConcurrencyException("Uygulama kaydı bulunamadı.");
        application.ConcurrencyToken = Guid.NewGuid().ToString("D");
        instance.BusinessKey = application.ClientId ?? application.Id;
        instance.State = application.ClientType ?? ClientTypes.Confidential;
        instance.Data = JsonSerializer.Serialize(application, OpenIddictStoreJson.Options);
        instance.Revision++;
        instance.UpdatedAt = DateTime.UtcNow;

        await db.SaveChangesAsync(cancellationToken);
    }

    private async Task<WorkflowInstance?> FindInstanceAsync(string identifier, CancellationToken cancellationToken)
    {
        if (Guid.TryParse(identifier, out var id))
        {
            return await db.Instances.FirstOrDefaultAsync(x => x.Id == id && x.WorkflowKey == WorkflowKey, cancellationToken);
        }

        var all = await db.Instances.Where(x => x.WorkflowKey == WorkflowKey).ToListAsync(cancellationToken);
        return all.FirstOrDefault(x => Deserialize(x).Id == identifier);
    }

    private async Task<List<WorkflowOpenIddictApplication>> LoadAllAsync(CancellationToken cancellationToken)
    {
        var instances = await db.Instances.AsNoTracking()
            .Where(x => x.WorkflowKey == WorkflowKey)
            .ToListAsync(cancellationToken);
        return [.. instances.Select(Deserialize)];
    }

    private static WorkflowOpenIddictApplication Deserialize(WorkflowInstance instance)
    {
        var app = JsonSerializer.Deserialize<WorkflowOpenIddictApplication>(instance.Data, OpenIddictStoreJson.Options)
            ?? new WorkflowOpenIddictApplication();
        if (string.IsNullOrEmpty(app.Id))
        {
            app.Id = instance.Id.ToString("D");
        }

        return app;
    }
}

internal sealed class WorkflowOpenIddictScopeStore(WorkflowDbContext db) : IOpenIddictScopeStore<WorkflowOpenIddictScope>
{
    internal const string WorkflowKey = "oauth-scope";

    public async ValueTask<long> CountAsync(CancellationToken cancellationToken) =>
        await db.Instances.AsNoTracking().LongCountAsync(x => x.WorkflowKey == WorkflowKey, cancellationToken);

    public async ValueTask<long> CountAsync<TResult>(Func<IQueryable<WorkflowOpenIddictScope>, IQueryable<TResult>> query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        var all = await LoadAllAsync(cancellationToken);
        return query(all.AsQueryable()).LongCount();
    }

    public async ValueTask CreateAsync(WorkflowOpenIddictScope scope, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(scope);
        var id = Guid.TryParse(scope.Id, out var parsed) && parsed != Guid.Empty ? parsed : db.NewId();
        scope.Id = id.ToString("D");
        scope.ConcurrencyToken = Guid.NewGuid().ToString("D");
        var now = DateTime.UtcNow;

        db.Instances.Add(new WorkflowInstance
        {
            Id = id,
            WorkflowKey = WorkflowKey,
            WorkflowVersion = 1,
            BusinessKey = scope.Name ?? scope.Id,
            State = "active",
            Status = WorkflowInstanceStatus.Active,
            Data = JsonSerializer.Serialize(scope, OpenIddictStoreJson.Options),
            Revision = 1,
            CreatedAt = now,
            UpdatedAt = now,
        });

        await db.SaveChangesAsync(cancellationToken);
    }

    public async ValueTask DeleteAsync(WorkflowOpenIddictScope scope, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(scope);
        var instance = await FindInstanceAsync(scope.Id, cancellationToken);
        if (instance is not null)
        {
            db.Instances.Remove(instance);
            await db.SaveChangesAsync(cancellationToken);
        }
    }

    public async ValueTask<WorkflowOpenIddictScope?> FindByIdAsync(string identifier, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrEmpty(identifier);
        var instance = await FindInstanceAsync(identifier, cancellationToken);
        return instance is null ? null : Deserialize(instance);
    }

    public async ValueTask<WorkflowOpenIddictScope?> FindByNameAsync(string name, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrEmpty(name);
        var instance = await db.Instances.AsNoTracking()
            .FirstOrDefaultAsync(x => x.WorkflowKey == WorkflowKey && x.BusinessKey == name, cancellationToken);
        if (instance is not null)
        {
            return Deserialize(instance);
        }

        var all = await LoadAllAsync(cancellationToken);
        return all.FirstOrDefault(s => string.Equals(s.Name, name, StringComparison.Ordinal));
    }

    public async IAsyncEnumerable<WorkflowOpenIddictScope> FindByNamesAsync(
        ImmutableArray<string> names,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var set = names.ToHashSet(StringComparer.Ordinal);
        foreach (var scope in await LoadAllAsync(cancellationToken))
        {
            if (scope.Name is not null && set.Contains(scope.Name))
            {
                yield return scope;
            }
        }
    }

    public async IAsyncEnumerable<WorkflowOpenIddictScope> FindByResourceAsync(
        string resource,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrEmpty(resource);
        foreach (var scope in await LoadAllAsync(cancellationToken))
        {
            if (scope.Resources.Contains(resource, StringComparer.Ordinal))
            {
                yield return scope;
            }
        }
    }

    public async ValueTask<TResult?> GetAsync<TState, TResult>(
        Func<IQueryable<WorkflowOpenIddictScope>, TState, IQueryable<TResult>> query,
        TState state,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        var all = await LoadAllAsync(cancellationToken);
        return query(all.AsQueryable(), state).FirstOrDefault();
    }

    public ValueTask<string?> GetDescriptionAsync(WorkflowOpenIddictScope scope, CancellationToken cancellationToken) =>
        new(scope.Description);

    public ValueTask<ImmutableDictionary<CultureInfo, string>> GetDescriptionsAsync(WorkflowOpenIddictScope scope, CancellationToken cancellationToken) =>
        new(OpenIddictStoreJson.ToCultureMap(scope.Descriptions));

    public ValueTask<string?> GetDisplayNameAsync(WorkflowOpenIddictScope scope, CancellationToken cancellationToken) =>
        new(scope.DisplayName);

    public ValueTask<ImmutableDictionary<CultureInfo, string>> GetDisplayNamesAsync(WorkflowOpenIddictScope scope, CancellationToken cancellationToken) =>
        new(OpenIddictStoreJson.ToCultureMap(scope.DisplayNames));

    public ValueTask<string?> GetIdAsync(WorkflowOpenIddictScope scope, CancellationToken cancellationToken) =>
        new(scope.Id);

    public ValueTask<string?> GetNameAsync(WorkflowOpenIddictScope scope, CancellationToken cancellationToken) =>
        new(scope.Name);

    public ValueTask<ImmutableDictionary<string, JsonElement>> GetPropertiesAsync(WorkflowOpenIddictScope scope, CancellationToken cancellationToken) =>
        new(OpenIddictStoreJson.ToPropertiesMap(scope.Properties));

    public ValueTask<ImmutableArray<string>> GetResourcesAsync(WorkflowOpenIddictScope scope, CancellationToken cancellationToken) =>
        new([.. scope.Resources]);

    public ValueTask<WorkflowOpenIddictScope> InstantiateAsync(CancellationToken cancellationToken) =>
        new(new WorkflowOpenIddictScope { Id = db.NewId().ToString("D") });

    public async IAsyncEnumerable<WorkflowOpenIddictScope> ListAsync(
        int? count,
        int? offset,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        IQueryable<WorkflowInstance> query = db.Instances.AsNoTracking()
            .Where(x => x.WorkflowKey == WorkflowKey)
            .OrderBy(x => x.BusinessKey);
        if (offset is { } skip)
        {
            query = query.Skip(skip);
        }

        if (count is { } take)
        {
            query = query.Take(take);
        }

        foreach (var instance in await query.ToListAsync(cancellationToken))
        {
            yield return Deserialize(instance);
        }
    }

    public async IAsyncEnumerable<TResult> ListAsync<TState, TResult>(
        Func<IQueryable<WorkflowOpenIddictScope>, TState, IQueryable<TResult>> query,
        TState state,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        var all = await LoadAllAsync(cancellationToken);
        foreach (var item in query(all.AsQueryable(), state))
        {
            yield return item;
        }
    }

    public ValueTask SetDescriptionAsync(WorkflowOpenIddictScope scope, string? description, CancellationToken cancellationToken)
    {
        scope.Description = description;
        return default;
    }

    public ValueTask SetDescriptionsAsync(WorkflowOpenIddictScope scope, ImmutableDictionary<CultureInfo, string> descriptions, CancellationToken cancellationToken)
    {
        scope.Descriptions = OpenIddictStoreJson.FromCultureMap(descriptions);
        return default;
    }

    public ValueTask SetDisplayNameAsync(WorkflowOpenIddictScope scope, string? name, CancellationToken cancellationToken)
    {
        scope.DisplayName = name;
        return default;
    }

    public ValueTask SetDisplayNamesAsync(WorkflowOpenIddictScope scope, ImmutableDictionary<CultureInfo, string> names, CancellationToken cancellationToken)
    {
        scope.DisplayNames = OpenIddictStoreJson.FromCultureMap(names);
        return default;
    }

    public ValueTask SetNameAsync(WorkflowOpenIddictScope scope, string? name, CancellationToken cancellationToken)
    {
        scope.Name = name;
        return default;
    }

    public ValueTask SetPropertiesAsync(WorkflowOpenIddictScope scope, ImmutableDictionary<string, JsonElement> properties, CancellationToken cancellationToken)
    {
        scope.Properties = OpenIddictStoreJson.FromPropertiesMap(properties);
        return default;
    }

    public ValueTask SetResourcesAsync(WorkflowOpenIddictScope scope, ImmutableArray<string> resources, CancellationToken cancellationToken)
    {
        scope.Resources = [.. resources];
        return default;
    }

    public async ValueTask UpdateAsync(WorkflowOpenIddictScope scope, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(scope);
        var instance = await FindInstanceAsync(scope.Id, cancellationToken)
            ?? throw new OpenIddictExceptions.ConcurrencyException("Kapsam kaydı bulunamadı.");
        scope.ConcurrencyToken = Guid.NewGuid().ToString("D");
        instance.BusinessKey = scope.Name ?? scope.Id;
        instance.Data = JsonSerializer.Serialize(scope, OpenIddictStoreJson.Options);
        instance.Revision++;
        instance.UpdatedAt = DateTime.UtcNow;

        await db.SaveChangesAsync(cancellationToken);
    }

    private async Task<WorkflowInstance?> FindInstanceAsync(string identifier, CancellationToken cancellationToken)
    {
        if (Guid.TryParse(identifier, out var id))
        {
            return await db.Instances.FirstOrDefaultAsync(x => x.Id == id && x.WorkflowKey == WorkflowKey, cancellationToken);
        }

        var all = await db.Instances.Where(x => x.WorkflowKey == WorkflowKey).ToListAsync(cancellationToken);
        return all.FirstOrDefault(x => Deserialize(x).Id == identifier);
    }

    private async Task<List<WorkflowOpenIddictScope>> LoadAllAsync(CancellationToken cancellationToken)
    {
        var instances = await db.Instances.AsNoTracking()
            .Where(x => x.WorkflowKey == WorkflowKey)
            .ToListAsync(cancellationToken);
        return [.. instances.Select(Deserialize)];
    }

    private static WorkflowOpenIddictScope Deserialize(WorkflowInstance instance)
    {
        var scope = JsonSerializer.Deserialize<WorkflowOpenIddictScope>(instance.Data, OpenIddictStoreJson.Options)
            ?? new WorkflowOpenIddictScope();
        if (string.IsNullOrEmpty(scope.Id))
        {
            scope.Id = instance.Id.ToString("D");
        }

        return scope;
    }
}

internal sealed class WorkflowOpenIddictAuthorizationStore(WorkflowDbContext db) : IOpenIddictAuthorizationStore<WorkflowOpenIddictAuthorization>
{
    internal const string WorkflowKey = "oauth-authorization";

    public async ValueTask<long> CountAsync(CancellationToken cancellationToken) =>
        await db.Instances.AsNoTracking().LongCountAsync(x => x.WorkflowKey == WorkflowKey, cancellationToken);

    public async ValueTask<long> CountAsync<TResult>(Func<IQueryable<WorkflowOpenIddictAuthorization>, IQueryable<TResult>> query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        var all = await LoadAllAsync(cancellationToken);
        return query(all.AsQueryable()).LongCount();
    }

    public async ValueTask CreateAsync(WorkflowOpenIddictAuthorization authorization, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(authorization);
        var id = Guid.TryParse(authorization.Id, out var parsed) && parsed != Guid.Empty ? parsed : db.NewId();
        authorization.Id = id.ToString("D");
        authorization.ConcurrencyToken = Guid.NewGuid().ToString("D");
        var now = authorization.CreationDate ?? DateTimeOffset.UtcNow;
        authorization.CreationDate = now;

        db.Instances.Add(new WorkflowInstance
        {
            Id = id,
            WorkflowKey = WorkflowKey,
            WorkflowVersion = 1,
            BusinessKey = authorization.Id,
            State = authorization.Status ?? Statuses.Valid,
            Status = string.Equals(authorization.Status, Statuses.Revoked, StringComparison.OrdinalIgnoreCase)
                ? WorkflowInstanceStatus.Completed
                : WorkflowInstanceStatus.Active,
            StartedBy = authorization.Subject,
            PendingTransition = authorization.ApplicationId,
            Data = JsonSerializer.Serialize(authorization, OpenIddictStoreJson.Options),
            Revision = 1,
            CreatedAt = now.UtcDateTime,
            UpdatedAt = DateTime.UtcNow,
        });

        await db.SaveChangesAsync(cancellationToken);
    }

    public async ValueTask DeleteAsync(WorkflowOpenIddictAuthorization authorization, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(authorization);
        var instance = await FindInstanceAsync(authorization.Id, cancellationToken);
        if (instance is not null)
        {
            db.Instances.Remove(instance);
        }

        var tokens = await db.Instances
            .Where(x => x.WorkflowKey == WorkflowOpenIddictTokenStore.WorkflowKey && x.PendingTransition == authorization.Id)
            .ToListAsync(cancellationToken);
        db.Instances.RemoveRange(tokens);

        await db.SaveChangesAsync(cancellationToken);
    }

    public async IAsyncEnumerable<WorkflowOpenIddictAuthorization> FindAsync(
        string? subject,
        string? client,
        string? status,
        string? type,
        ImmutableArray<string>? scopes,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var query = db.Instances.AsNoTracking().Where(x => x.WorkflowKey == WorkflowKey);
        if (!string.IsNullOrEmpty(subject))
        {
            query = query.Where(x => x.StartedBy == subject);
        }

        if (!string.IsNullOrEmpty(client))
        {
            query = query.Where(x => x.PendingTransition == client);
        }

        foreach (var instance in await query.ToListAsync(cancellationToken))
        {
            var auth = Deserialize(instance);
            if (!string.IsNullOrEmpty(subject) && !string.Equals(auth.Subject, subject, StringComparison.Ordinal))
            {
                continue;
            }

            if (!string.IsNullOrEmpty(client) && !string.Equals(auth.ApplicationId, client, StringComparison.Ordinal))
            {
                continue;
            }

            if (!string.IsNullOrEmpty(status) && !string.Equals(auth.Status, status, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (!string.IsNullOrEmpty(type) && !string.Equals(auth.Type, type, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (scopes is { } required && !required.All(s => auth.Scopes.Contains(s, StringComparer.Ordinal)))
            {
                continue;
            }

            yield return auth;
        }
    }

    public async IAsyncEnumerable<WorkflowOpenIddictAuthorization> FindByApplicationIdAsync(
        string identifier,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrEmpty(identifier);
        var instances = await db.Instances.AsNoTracking()
            .Where(x => x.WorkflowKey == WorkflowKey && x.PendingTransition == identifier)
            .ToListAsync(cancellationToken);
        foreach (var instance in instances)
        {
            yield return Deserialize(instance);
        }
    }

    public async ValueTask<WorkflowOpenIddictAuthorization?> FindByIdAsync(string identifier, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrEmpty(identifier);
        var instance = await FindInstanceAsync(identifier, cancellationToken);
        return instance is null ? null : Deserialize(instance);
    }

    public async IAsyncEnumerable<WorkflowOpenIddictAuthorization> FindBySubjectAsync(
        string subject,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrEmpty(subject);
        var instances = await db.Instances.AsNoTracking()
            .Where(x => x.WorkflowKey == WorkflowKey && x.StartedBy == subject)
            .ToListAsync(cancellationToken);
        foreach (var instance in instances)
        {
            yield return Deserialize(instance);
        }
    }

    public ValueTask<string?> GetApplicationIdAsync(WorkflowOpenIddictAuthorization authorization, CancellationToken cancellationToken) =>
        new(authorization.ApplicationId);

    public async ValueTask<TResult?> GetAsync<TState, TResult>(
        Func<IQueryable<WorkflowOpenIddictAuthorization>, TState, IQueryable<TResult>> query,
        TState state,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        var all = await LoadAllAsync(cancellationToken);
        return query(all.AsQueryable(), state).FirstOrDefault();
    }

    public ValueTask<DateTimeOffset?> GetCreationDateAsync(WorkflowOpenIddictAuthorization authorization, CancellationToken cancellationToken) =>
        new(authorization.CreationDate);

    public ValueTask<string?> GetIdAsync(WorkflowOpenIddictAuthorization authorization, CancellationToken cancellationToken) =>
        new(authorization.Id);

    public ValueTask<ImmutableDictionary<string, JsonElement>> GetPropertiesAsync(WorkflowOpenIddictAuthorization authorization, CancellationToken cancellationToken) =>
        new(OpenIddictStoreJson.ToPropertiesMap(authorization.Properties));

    public ValueTask<ImmutableArray<string>> GetScopesAsync(WorkflowOpenIddictAuthorization authorization, CancellationToken cancellationToken) =>
        new([.. authorization.Scopes]);

    public ValueTask<string?> GetStatusAsync(WorkflowOpenIddictAuthorization authorization, CancellationToken cancellationToken) =>
        new(authorization.Status);

    public ValueTask<string?> GetSubjectAsync(WorkflowOpenIddictAuthorization authorization, CancellationToken cancellationToken) =>
        new(authorization.Subject);

    public ValueTask<string?> GetTypeAsync(WorkflowOpenIddictAuthorization authorization, CancellationToken cancellationToken) =>
        new(authorization.Type);

    public ValueTask<WorkflowOpenIddictAuthorization> InstantiateAsync(CancellationToken cancellationToken) =>
        new(new WorkflowOpenIddictAuthorization { Id = db.NewId().ToString("D") });

    public async IAsyncEnumerable<WorkflowOpenIddictAuthorization> ListAsync(
        int? count,
        int? offset,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        IQueryable<WorkflowInstance> query = db.Instances.AsNoTracking()
            .Where(x => x.WorkflowKey == WorkflowKey)
            .OrderByDescending(x => x.CreatedAt);
        if (offset is { } skip)
        {
            query = query.Skip(skip);
        }

        if (count is { } take)
        {
            query = query.Take(take);
        }

        foreach (var instance in await query.ToListAsync(cancellationToken))
        {
            yield return Deserialize(instance);
        }
    }

    public async IAsyncEnumerable<TResult> ListAsync<TState, TResult>(
        Func<IQueryable<WorkflowOpenIddictAuthorization>, TState, IQueryable<TResult>> query,
        TState state,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        var all = await LoadAllAsync(cancellationToken);
        foreach (var item in query(all.AsQueryable(), state))
        {
            yield return item;
        }
    }

    public async ValueTask<long> PruneAsync(DateTimeOffset threshold, CancellationToken cancellationToken)
    {
        var cutoff = threshold.UtcDateTime;
        var instances = await db.Instances
            .Where(x => x.WorkflowKey == WorkflowKey && x.CreatedAt < cutoff)
            .ToListAsync(cancellationToken);
        long pruned = 0;
        foreach (var instance in instances)
        {
            var auth = Deserialize(instance);
            if (!string.Equals(auth.Status, Statuses.Valid, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(auth.Type, AuthorizationTypes.AdHoc, StringComparison.OrdinalIgnoreCase))
            {
                db.Instances.Remove(instance);
                pruned++;
            }
        }

        if (pruned > 0)
        {
            await db.SaveChangesAsync(cancellationToken);
        }

        return pruned;
    }

    public async ValueTask<long> RevokeAsync(
        string? subject,
        string? client,
        string? status,
        string? type,
        CancellationToken cancellationToken)
    {
        var instances = await db.Instances
            .Where(x => x.WorkflowKey == WorkflowKey)
            .ToListAsync(cancellationToken);
        long count = 0;
        foreach (var instance in instances)
        {
            var auth = Deserialize(instance);
            if (!string.IsNullOrEmpty(subject) && !string.Equals(auth.Subject, subject, StringComparison.Ordinal))
            {
                continue;
            }

            if (!string.IsNullOrEmpty(client) && !string.Equals(auth.ApplicationId, client, StringComparison.Ordinal))
            {
                continue;
            }

            if (!string.IsNullOrEmpty(status) && !string.Equals(auth.Status, status, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (!string.IsNullOrEmpty(type) && !string.Equals(auth.Type, type, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (!string.Equals(auth.Status, Statuses.Revoked, StringComparison.OrdinalIgnoreCase))
            {
                auth.Status = Statuses.Revoked;
                ApplyToInstance(instance, auth);
                count++;
            }
        }

        if (count > 0)
        {
            await db.SaveChangesAsync(cancellationToken);
        }

        return count;
    }

    public ValueTask<long> RevokeByApplicationIdAsync(string identifier, CancellationToken cancellationToken) =>
        RevokeAsync(subject: null, client: identifier, status: null, type: null, cancellationToken);

    public ValueTask<long> RevokeBySubjectAsync(string subject, CancellationToken cancellationToken) =>
        RevokeAsync(subject: subject, client: null, status: null, type: null, cancellationToken);

    public ValueTask SetApplicationIdAsync(WorkflowOpenIddictAuthorization authorization, string? identifier, CancellationToken cancellationToken)
    {
        authorization.ApplicationId = identifier;
        return default;
    }

    public ValueTask SetCreationDateAsync(WorkflowOpenIddictAuthorization authorization, DateTimeOffset? date, CancellationToken cancellationToken)
    {
        authorization.CreationDate = date;
        return default;
    }

    public ValueTask SetPropertiesAsync(WorkflowOpenIddictAuthorization authorization, ImmutableDictionary<string, JsonElement> properties, CancellationToken cancellationToken)
    {
        authorization.Properties = OpenIddictStoreJson.FromPropertiesMap(properties);
        return default;
    }

    public ValueTask SetScopesAsync(WorkflowOpenIddictAuthorization authorization, ImmutableArray<string> scopes, CancellationToken cancellationToken)
    {
        authorization.Scopes = [.. scopes];
        return default;
    }

    public ValueTask SetStatusAsync(WorkflowOpenIddictAuthorization authorization, string? status, CancellationToken cancellationToken)
    {
        authorization.Status = status;
        return default;
    }

    public ValueTask SetSubjectAsync(WorkflowOpenIddictAuthorization authorization, string? subject, CancellationToken cancellationToken)
    {
        authorization.Subject = subject;
        return default;
    }

    public ValueTask SetTypeAsync(WorkflowOpenIddictAuthorization authorization, string? type, CancellationToken cancellationToken)
    {
        authorization.Type = type;
        return default;
    }

    public async ValueTask UpdateAsync(WorkflowOpenIddictAuthorization authorization, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(authorization);
        var instance = await FindInstanceAsync(authorization.Id, cancellationToken)
            ?? throw new OpenIddictExceptions.ConcurrencyException("Yetkilendirme kaydı bulunamadı.");
        ApplyToInstance(instance, authorization);
        await db.SaveChangesAsync(cancellationToken);
    }

    private static void ApplyToInstance(WorkflowInstance instance, WorkflowOpenIddictAuthorization authorization)
    {
        authorization.ConcurrencyToken = Guid.NewGuid().ToString("D");
        instance.BusinessKey = authorization.Id;
        instance.StartedBy = authorization.Subject;
        instance.PendingTransition = authorization.ApplicationId;
        instance.State = authorization.Status ?? Statuses.Valid;
        instance.Status = string.Equals(authorization.Status, Statuses.Revoked, StringComparison.OrdinalIgnoreCase)
            ? WorkflowInstanceStatus.Completed
            : WorkflowInstanceStatus.Active;
        instance.Data = JsonSerializer.Serialize(authorization, OpenIddictStoreJson.Options);
        instance.Revision++;
        instance.UpdatedAt = DateTime.UtcNow;
    }

    private async Task<WorkflowInstance?> FindInstanceAsync(string identifier, CancellationToken cancellationToken)
    {
        if (Guid.TryParse(identifier, out var id))
        {
            return await db.Instances.FirstOrDefaultAsync(x => x.Id == id && x.WorkflowKey == WorkflowKey, cancellationToken);
        }

        var all = await db.Instances.Where(x => x.WorkflowKey == WorkflowKey).ToListAsync(cancellationToken);
        return all.FirstOrDefault(x => Deserialize(x).Id == identifier);
    }

    private async Task<List<WorkflowOpenIddictAuthorization>> LoadAllAsync(CancellationToken cancellationToken)
    {
        var instances = await db.Instances.AsNoTracking()
            .Where(x => x.WorkflowKey == WorkflowKey)
            .ToListAsync(cancellationToken);
        return [.. instances.Select(Deserialize)];
    }

    internal static WorkflowOpenIddictAuthorization Deserialize(WorkflowInstance instance)
    {
        var auth = JsonSerializer.Deserialize<WorkflowOpenIddictAuthorization>(instance.Data, OpenIddictStoreJson.Options)
            ?? new WorkflowOpenIddictAuthorization();
        if (string.IsNullOrEmpty(auth.Id))
        {
            auth.Id = instance.Id.ToString("D");
        }

        return auth;
    }
}

internal sealed class WorkflowOpenIddictTokenStore(WorkflowDbContext db) : IOpenIddictTokenStore<WorkflowOpenIddictToken>
{
    internal const string WorkflowKey = "oauth-token";

    public async ValueTask<long> CountAsync(CancellationToken cancellationToken) =>
        await db.Instances.AsNoTracking().LongCountAsync(x => x.WorkflowKey == WorkflowKey, cancellationToken);

    public async ValueTask<long> CountAsync<TResult>(Func<IQueryable<WorkflowOpenIddictToken>, IQueryable<TResult>> query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        var all = await LoadAllAsync(cancellationToken);
        return query(all.AsQueryable()).LongCount();
    }

    public async ValueTask CreateAsync(WorkflowOpenIddictToken token, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(token);
        var id = Guid.TryParse(token.Id, out var parsed) && parsed != Guid.Empty ? parsed : db.NewId();
        token.Id = id.ToString("D");
        token.ConcurrencyToken = Guid.NewGuid().ToString("D");
        var now = token.CreationDate ?? DateTimeOffset.UtcNow;
        token.CreationDate = now;

        db.Instances.Add(new WorkflowInstance
        {
            Id = id,
            WorkflowKey = WorkflowKey,
            WorkflowVersion = 1,
            BusinessKey = token.ReferenceId ?? token.Id,
            State = token.Status ?? Statuses.Valid,
            Status = string.Equals(token.Status, Statuses.Valid, StringComparison.OrdinalIgnoreCase)
                ? WorkflowInstanceStatus.Active
                : WorkflowInstanceStatus.Completed,
            StartedBy = token.Subject,
            PendingTransition = token.AuthorizationId,
            Data = JsonSerializer.Serialize(token, OpenIddictStoreJson.Options),
            Revision = 1,
            CreatedAt = now.UtcDateTime,
            UpdatedAt = DateTime.UtcNow,
        });

        await db.SaveChangesAsync(cancellationToken);
    }

    public async ValueTask DeleteAsync(WorkflowOpenIddictToken token, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(token);
        var instance = await FindInstanceAsync(token.Id, cancellationToken);
        if (instance is not null)
        {
            db.Instances.Remove(instance);
            await db.SaveChangesAsync(cancellationToken);
        }
    }

    public async IAsyncEnumerable<WorkflowOpenIddictToken> FindAsync(
        string? subject,
        string? client,
        string? status,
        string? type,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var query = db.Instances.AsNoTracking().Where(x => x.WorkflowKey == WorkflowKey);
        if (!string.IsNullOrEmpty(subject))
        {
            query = query.Where(x => x.StartedBy == subject);
        }

        foreach (var instance in await query.ToListAsync(cancellationToken))
        {
            var token = Deserialize(instance);
            if (!string.IsNullOrEmpty(subject) && !string.Equals(token.Subject, subject, StringComparison.Ordinal))
            {
                continue;
            }

            if (!string.IsNullOrEmpty(client) && !string.Equals(token.ApplicationId, client, StringComparison.Ordinal))
            {
                continue;
            }

            if (!string.IsNullOrEmpty(status) && !string.Equals(token.Status, status, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (!string.IsNullOrEmpty(type) && !string.Equals(token.Type, type, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            yield return token;
        }
    }

    public async IAsyncEnumerable<WorkflowOpenIddictToken> FindByApplicationIdAsync(
        string identifier,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrEmpty(identifier);
        foreach (var token in await LoadAllAsync(cancellationToken))
        {
            if (string.Equals(token.ApplicationId, identifier, StringComparison.Ordinal))
            {
                yield return token;
            }
        }
    }

    public async IAsyncEnumerable<WorkflowOpenIddictToken> FindByAuthorizationIdAsync(
        string identifier,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrEmpty(identifier);
        var instances = await db.Instances.AsNoTracking()
            .Where(x => x.WorkflowKey == WorkflowKey && x.PendingTransition == identifier)
            .ToListAsync(cancellationToken);
        foreach (var instance in instances)
        {
            yield return Deserialize(instance);
        }
    }

    public async ValueTask<WorkflowOpenIddictToken?> FindByIdAsync(string identifier, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrEmpty(identifier);
        var instance = await FindInstanceAsync(identifier, cancellationToken);
        return instance is null ? null : Deserialize(instance);
    }

    public async ValueTask<WorkflowOpenIddictToken?> FindByReferenceIdAsync(string identifier, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrEmpty(identifier);
        var instance = await db.Instances.AsNoTracking()
            .FirstOrDefaultAsync(x => x.WorkflowKey == WorkflowKey && x.BusinessKey == identifier, cancellationToken);
        if (instance is not null)
        {
            return Deserialize(instance);
        }

        var all = await LoadAllAsync(cancellationToken);
        return all.FirstOrDefault(t => string.Equals(t.ReferenceId, identifier, StringComparison.Ordinal));
    }

    public async IAsyncEnumerable<WorkflowOpenIddictToken> FindBySubjectAsync(
        string subject,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrEmpty(subject);
        var instances = await db.Instances.AsNoTracking()
            .Where(x => x.WorkflowKey == WorkflowKey && x.StartedBy == subject)
            .ToListAsync(cancellationToken);
        foreach (var instance in instances)
        {
            yield return Deserialize(instance);
        }
    }

    public ValueTask<string?> GetApplicationIdAsync(WorkflowOpenIddictToken token, CancellationToken cancellationToken) =>
        new(token.ApplicationId);

    public async ValueTask<TResult?> GetAsync<TState, TResult>(
        Func<IQueryable<WorkflowOpenIddictToken>, TState, IQueryable<TResult>> query,
        TState state,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        var all = await LoadAllAsync(cancellationToken);
        return query(all.AsQueryable(), state).FirstOrDefault();
    }

    public ValueTask<string?> GetAuthorizationIdAsync(WorkflowOpenIddictToken token, CancellationToken cancellationToken) =>
        new(token.AuthorizationId);

    public ValueTask<DateTimeOffset?> GetCreationDateAsync(WorkflowOpenIddictToken token, CancellationToken cancellationToken) =>
        new(token.CreationDate);

    public ValueTask<DateTimeOffset?> GetExpirationDateAsync(WorkflowOpenIddictToken token, CancellationToken cancellationToken) =>
        new(token.ExpirationDate);

    public ValueTask<string?> GetIdAsync(WorkflowOpenIddictToken token, CancellationToken cancellationToken) =>
        new(token.Id);

    public ValueTask<string?> GetPayloadAsync(WorkflowOpenIddictToken token, CancellationToken cancellationToken) =>
        new(token.Payload);

    public ValueTask<ImmutableDictionary<string, JsonElement>> GetPropertiesAsync(WorkflowOpenIddictToken token, CancellationToken cancellationToken) =>
        new(OpenIddictStoreJson.ToPropertiesMap(token.Properties));

    public ValueTask<DateTimeOffset?> GetRedemptionDateAsync(WorkflowOpenIddictToken token, CancellationToken cancellationToken) =>
        new(token.RedemptionDate);

    public ValueTask<string?> GetReferenceIdAsync(WorkflowOpenIddictToken token, CancellationToken cancellationToken) =>
        new(token.ReferenceId);

    public ValueTask<string?> GetStatusAsync(WorkflowOpenIddictToken token, CancellationToken cancellationToken) =>
        new(token.Status);

    public ValueTask<string?> GetSubjectAsync(WorkflowOpenIddictToken token, CancellationToken cancellationToken) =>
        new(token.Subject);

    public ValueTask<string?> GetTypeAsync(WorkflowOpenIddictToken token, CancellationToken cancellationToken) =>
        new(token.Type);

    public ValueTask<WorkflowOpenIddictToken> InstantiateAsync(CancellationToken cancellationToken) =>
        new(new WorkflowOpenIddictToken { Id = db.NewId().ToString("D") });

    public async IAsyncEnumerable<WorkflowOpenIddictToken> ListAsync(
        int? count,
        int? offset,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        IQueryable<WorkflowInstance> query = db.Instances.AsNoTracking()
            .Where(x => x.WorkflowKey == WorkflowKey)
            .OrderByDescending(x => x.CreatedAt);
        if (offset is { } skip)
        {
            query = query.Skip(skip);
        }

        if (count is { } take)
        {
            query = query.Take(take);
        }

        foreach (var instance in await query.ToListAsync(cancellationToken))
        {
            yield return Deserialize(instance);
        }
    }

    public async IAsyncEnumerable<TResult> ListAsync<TState, TResult>(
        Func<IQueryable<WorkflowOpenIddictToken>, TState, IQueryable<TResult>> query,
        TState state,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        var all = await LoadAllAsync(cancellationToken);
        foreach (var item in query(all.AsQueryable(), state))
        {
            yield return item;
        }
    }

    public async ValueTask<long> PruneAsync(DateTimeOffset threshold, CancellationToken cancellationToken)
    {
        var now = DateTimeOffset.UtcNow;
        var cutoff = threshold.UtcDateTime;
        var instances = await db.Instances
            .Where(x => x.WorkflowKey == WorkflowKey && x.CreatedAt < cutoff)
            .ToListAsync(cancellationToken);
        long pruned = 0;
        foreach (var instance in instances)
        {
            var token = Deserialize(instance);
            if (!string.Equals(token.Status, Statuses.Valid, StringComparison.OrdinalIgnoreCase) ||
                (token.ExpirationDate is { } exp && exp < now))
            {
                db.Instances.Remove(instance);
                pruned++;
            }
        }

        if (pruned > 0)
        {
            await db.SaveChangesAsync(cancellationToken);
        }

        return pruned;
    }

    public async ValueTask<long> RevokeAsync(
        string? subject,
        string? client,
        string? status,
        string? type,
        CancellationToken cancellationToken)
    {
        var instances = await db.Instances
            .Where(x => x.WorkflowKey == WorkflowKey)
            .ToListAsync(cancellationToken);
        long count = 0;
        foreach (var instance in instances)
        {
            var token = Deserialize(instance);
            if (!string.IsNullOrEmpty(subject) && !string.Equals(token.Subject, subject, StringComparison.Ordinal))
            {
                continue;
            }

            if (!string.IsNullOrEmpty(client) && !string.Equals(token.ApplicationId, client, StringComparison.Ordinal))
            {
                continue;
            }

            if (!string.IsNullOrEmpty(status) && !string.Equals(token.Status, status, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (!string.IsNullOrEmpty(type) && !string.Equals(token.Type, type, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (!string.Equals(token.Status, Statuses.Revoked, StringComparison.OrdinalIgnoreCase))
            {
                token.Status = Statuses.Revoked;
                ApplyToInstance(instance, token);
                count++;
            }
        }

        if (count > 0)
        {
            await db.SaveChangesAsync(cancellationToken);
        }

        return count;
    }

    public ValueTask<long> RevokeByApplicationIdAsync(string identifier, CancellationToken cancellationToken) =>
        RevokeAsync(subject: null, client: identifier, status: null, type: null, cancellationToken);

    public async ValueTask<long> RevokeByAuthorizationIdAsync(string identifier, CancellationToken cancellationToken)
    {
        var instances = await db.Instances
            .Where(x => x.WorkflowKey == WorkflowKey && x.PendingTransition == identifier)
            .ToListAsync(cancellationToken);
        long count = 0;
        foreach (var instance in instances)
        {
            var token = Deserialize(instance);
            if (!string.Equals(token.Status, Statuses.Revoked, StringComparison.OrdinalIgnoreCase))
            {
                token.Status = Statuses.Revoked;
                ApplyToInstance(instance, token);
                count++;
            }
        }

        if (count > 0)
        {
            await db.SaveChangesAsync(cancellationToken);
        }

        return count;
    }

    public ValueTask<long> RevokeBySubjectAsync(string subject, CancellationToken cancellationToken) =>
        RevokeAsync(subject: subject, client: null, status: null, type: null, cancellationToken);

    public ValueTask SetApplicationIdAsync(WorkflowOpenIddictToken token, string? identifier, CancellationToken cancellationToken)
    {
        token.ApplicationId = identifier;
        return default;
    }

    public ValueTask SetAuthorizationIdAsync(WorkflowOpenIddictToken token, string? identifier, CancellationToken cancellationToken)
    {
        token.AuthorizationId = identifier;
        return default;
    }

    public ValueTask SetCreationDateAsync(WorkflowOpenIddictToken token, DateTimeOffset? date, CancellationToken cancellationToken)
    {
        token.CreationDate = date;
        return default;
    }

    public ValueTask SetExpirationDateAsync(WorkflowOpenIddictToken token, DateTimeOffset? date, CancellationToken cancellationToken)
    {
        token.ExpirationDate = date;
        return default;
    }

    public ValueTask SetPayloadAsync(WorkflowOpenIddictToken token, string? payload, CancellationToken cancellationToken)
    {
        token.Payload = payload;
        return default;
    }

    public ValueTask SetPropertiesAsync(WorkflowOpenIddictToken token, ImmutableDictionary<string, JsonElement> properties, CancellationToken cancellationToken)
    {
        token.Properties = OpenIddictStoreJson.FromPropertiesMap(properties);
        return default;
    }

    public ValueTask SetRedemptionDateAsync(WorkflowOpenIddictToken token, DateTimeOffset? date, CancellationToken cancellationToken)
    {
        token.RedemptionDate = date;
        return default;
    }

    public ValueTask SetReferenceIdAsync(WorkflowOpenIddictToken token, string? identifier, CancellationToken cancellationToken)
    {
        token.ReferenceId = identifier;
        return default;
    }

    public ValueTask SetStatusAsync(WorkflowOpenIddictToken token, string? status, CancellationToken cancellationToken)
    {
        token.Status = status;
        return default;
    }

    public ValueTask SetSubjectAsync(WorkflowOpenIddictToken token, string? subject, CancellationToken cancellationToken)
    {
        token.Subject = subject;
        return default;
    }

    public ValueTask SetTypeAsync(WorkflowOpenIddictToken token, string? type, CancellationToken cancellationToken)
    {
        token.Type = type;
        return default;
    }

    public async ValueTask UpdateAsync(WorkflowOpenIddictToken token, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(token);
        var instance = await FindInstanceAsync(token.Id, cancellationToken)
            ?? throw new OpenIddictExceptions.ConcurrencyException("Token kaydı bulunamadı.");
        ApplyToInstance(instance, token);
        await db.SaveChangesAsync(cancellationToken);
    }

    private static void ApplyToInstance(WorkflowInstance instance, WorkflowOpenIddictToken token)
    {
        token.ConcurrencyToken = Guid.NewGuid().ToString("D");
        instance.BusinessKey = token.ReferenceId ?? token.Id;
        instance.StartedBy = token.Subject;
        instance.PendingTransition = token.AuthorizationId;
        instance.State = token.Status ?? Statuses.Valid;
        instance.Status = string.Equals(token.Status, Statuses.Valid, StringComparison.OrdinalIgnoreCase)
            ? WorkflowInstanceStatus.Active
            : WorkflowInstanceStatus.Completed;
        instance.Data = JsonSerializer.Serialize(token, OpenIddictStoreJson.Options);
        instance.Revision++;
        instance.UpdatedAt = DateTime.UtcNow;
    }

    private async Task<WorkflowInstance?> FindInstanceAsync(string identifier, CancellationToken cancellationToken)
    {
        if (Guid.TryParse(identifier, out var id))
        {
            return await db.Instances.FirstOrDefaultAsync(x => x.Id == id && x.WorkflowKey == WorkflowKey, cancellationToken);
        }

        var all = await db.Instances.Where(x => x.WorkflowKey == WorkflowKey).ToListAsync(cancellationToken);
        return all.FirstOrDefault(x => Deserialize(x).Id == identifier);
    }

    private async Task<List<WorkflowOpenIddictToken>> LoadAllAsync(CancellationToken cancellationToken)
    {
        var instances = await db.Instances.AsNoTracking()
            .Where(x => x.WorkflowKey == WorkflowKey)
            .ToListAsync(cancellationToken);
        return [.. instances.Select(Deserialize)];
    }

    internal static WorkflowOpenIddictToken Deserialize(WorkflowInstance instance)
    {
        var token = JsonSerializer.Deserialize<WorkflowOpenIddictToken>(instance.Data, OpenIddictStoreJson.Options)
            ?? new WorkflowOpenIddictToken();
        if (string.IsNullOrEmpty(token.Id))
        {
            token.Id = instance.Id.ToString("D");
        }

        return token;
    }
}
