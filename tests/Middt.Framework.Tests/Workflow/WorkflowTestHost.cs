using System.Collections.Concurrent;
using System.Net;
using System.Security.Claims;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using Middt.Framework.Model.Workflow;
using Middt.Framework.Workflow;
using Middt.Framework.Workflow.DependencyInjection;
using Middt.Framework.Workflow.Endpoints;
using Middt.Framework.Workflow.Expressions;
using Middt.Framework.Workflow.Persistence;
using Middt.Framework.Workflow.Runtime;
using Middt.Framework.Workflow.Tasks;

namespace Middt.Framework.Tests.Workflow;

/// <summary>
/// In-process workflow host: SQLite in-memory (connection open for the test lifetime), <see cref="FakeTimeProvider"/>,
/// job processor driven manually through <see cref="RunJobsAsync"/>, and a TestServer exposing <c>MapMiddtWorkflows</c>
/// with a header based test authentication (<c>X-Test-User</c>, <c>X-Test-Roles</c>).
/// </summary>
public sealed class WorkflowTestHost : IAsyncDisposable
{
    public static readonly DateTimeOffset StartTime = new(2026, 1, 1, 8, 0, 0, TimeSpan.Zero);

    private readonly bool ownsConnection;

    private WorkflowTestHost(WebApplication app, SqliteConnection connection, bool ownsConnection, FakeTimeProvider time)
    {
        App = app;
        Connection = connection;
        this.ownsConnection = ownsConnection;
        Time = time;
        Client = app.GetTestClient();
    }

    public WebApplication App { get; }

    public IServiceProvider Services => App.Services;

    public SqliteConnection Connection { get; }

    public FakeTimeProvider Time { get; }

    public HttpClient Client { get; }

    public TaskLog Log => Services.GetRequiredService<TaskLog>();

    public RecordingEventHandler Events => Services.GetRequiredService<RecordingEventHandler>();

    public WorkflowJobProcessor Processor => Services.GetRequiredService<WorkflowJobProcessor>();

    public static Task<WorkflowTestHost> StartAsync(params string[] definitions) => StartAsync(definitions, null);

    public static async Task<WorkflowTestHost> StartAsync(
        IEnumerable<string> definitions,
        Action<MiddtWorkflowOptions>? configure = null,
        Action<IServiceCollection>? services = null,
        Action<DbContextOptionsBuilder>? configureDb = null,
        SqliteConnection? connection = null,
        FakeTimeProvider? time = null)
    {
        var ownsConnection = connection is null;
        if (connection is null)
        {
            connection = new SqliteConnection("DataSource=:memory:");
            await connection.OpenAsync();
        }

        time ??= new FakeTimeProvider(StartTime);

        var builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            EnvironmentName = "Development",
            ApplicationName = typeof(WorkflowTestHost).Assembly.GetName().Name,
            ContentRootPath = AppContext.BaseDirectory,
        });
        builder.WebHost.UseTestServer();
        builder.Logging.ClearProviders();

        // Before AddMiddtWorkflow (it uses TryAdd).
        builder.Services.AddSingleton<TimeProvider>(time);
        builder.Services.AddSingleton<TaskLog>();
        builder.Services.AddSingleton<FlakyCounter>();
        builder.Services.AddSingleton<RecordingEventHandler>();
        builder.Services.AddSingleton<IWorkflowEventHandler>(sp => sp.GetRequiredService<RecordingEventHandler>());

        var sqlite = connection;
        var definitionList = definitions.ToList();
        builder.Services.AddMiddtWorkflow(o =>
        {
            o.UseDbContext(db =>
            {
                db.UseSqlite(sqlite);
                configureDb?.Invoke(db);
            });
            o.AutoCreateSchema = true;
            o.EnableJobProcessor = false;
            foreach (var json in definitionList)
            {
                o.AddDefinition(json, "test-" + definitionList.IndexOf(json));
            }

            o.AddTask<RecordTask>("record");
            o.AddTask<FlakyTask>("flaky");
            o.AddTask<FailTask>("fail");
            configure?.Invoke(o);
        });

        // EnableJobProcessor = false: register the processor without the hosted loop so tests drive it.
        builder.Services.AddSingleton<WorkflowJobProcessor>();

        builder.Services.AddAuthentication(TestAuthHandler.SchemeName)
            .AddScheme<AuthenticationSchemeOptions, TestAuthHandler>(TestAuthHandler.SchemeName, null);
        builder.Services.AddAuthorization();
        services?.Invoke(builder.Services);

        var app = builder.Build();
        app.UseAuthentication();
        app.UseAuthorization();
        app.MapMiddtWorkflows("api/workflows");

        try
        {
            await app.StartAsync();
        }
        catch
        {
            await app.DisposeAsync();
            if (ownsConnection)
            {
                await connection.DisposeAsync();
            }

            throw;
        }

        return new WorkflowTestHost(app, connection, ownsConnection, time);
    }

    public static WorkflowUser User(string name = "alice", params string[] roles) => new() { Id = name, Name = name, Roles = roles };

    public async Task<T> WithEngineAsync<T>(Func<WorkflowEngine, Task<T>> action)
    {
        await using var scope = Services.CreateAsyncScope();
        return await action(scope.ServiceProvider.GetRequiredService<WorkflowEngine>());
    }

    public async Task<WorkflowInstanceModel> StartInstanceAsync(string workflow, object? data = null, string? key = null, WorkflowUser? user = null)
    {
        var (instance, _) = await StartWithFlagAsync(workflow, data, key, user);
        return instance;
    }

    public Task<(WorkflowInstanceModel Instance, bool Created)> StartWithFlagAsync(string workflow, object? data = null, string? key = null, WorkflowUser? user = null) =>
        WithEngineAsync(e => e.StartAsync(workflow, new StartWorkflowRequest { Key = key, Data = ToObject(data) }, user ?? User(), TestContext.Current.CancellationToken));

    public Task<WorkflowInstanceModel> TransitionAsync(Guid id, string transition, object? input = null, WorkflowUser? user = null, bool runAsync = false) =>
        WithEngineAsync(e => e.ExecuteTransitionAsync(id, transition, ToObject(input), user ?? User(), runAsync, TestContext.Current.CancellationToken));

    public Task<WorkflowInstanceModel> GetAsync(Guid id, WorkflowUser? user = null) =>
        WithEngineAsync(e => e.GetAsync(id, user ?? WorkflowUser.System, TestContext.Current.CancellationToken));

    public Task<WorkflowInstanceModel> RetryAsync(Guid id, WorkflowUser? user = null) =>
        WithEngineAsync(e => e.RetryAsync(id, user ?? User(), TestContext.Current.CancellationToken));

    public Task<List<WorkflowIncidentModel>> IncidentsAsync(Guid id) =>
        WithEngineAsync(e => e.IncidentsAsync(id, WorkflowUser.System, TestContext.Current.CancellationToken));

    public Task<List<WorkflowHistoryModel>> HistoryAsync(Guid id) =>
        WithEngineAsync(e => e.HistoryAsync(id, WorkflowUser.System, TestContext.Current.CancellationToken));

    public async Task<T> WithDbAsync<T>(Func<WorkflowDbContext, Task<T>> action)
    {
        await using var scope = Services.CreateAsyncScope();
        return await action(scope.ServiceProvider.GetRequiredService<WorkflowDbContext>());
    }

    public Task<List<WorkflowJob>> JobsAsync(Guid instanceId) =>
        WithDbAsync(db => db.Jobs.AsNoTracking().Where(j => j.InstanceId == instanceId).ToListAsync(TestContext.Current.CancellationToken));

    public Task<WorkflowInstance> RowAsync(Guid instanceId) =>
        WithDbAsync(db => db.Instances.AsNoTracking().SingleAsync(i => i.Id == instanceId, TestContext.Current.CancellationToken));

    public Task<int> SetStatusAsync(Guid instanceId, WorkflowInstanceStatus status, DateTime? busySince = null) =>
        WithDbAsync(db => db.Instances.Where(i => i.Id == instanceId)
            .ExecuteUpdateAsync(s => s.SetProperty(i => i.Status, status).SetProperty(i => i.BusySince, busySince), TestContext.Current.CancellationToken));

    /// <summary>Advances the fake clock and processes due jobs until none are left.</summary>
    public async Task<int> RunJobsAsync(TimeSpan? advance = null)
    {
        if (advance is { } by)
        {
            Time.Advance(by);
        }

        var total = 0;
        for (var i = 0; i < 10; i++)
        {
            var processed = await Processor.RunOnceAsync(TestContext.Current.CancellationToken);
            total += processed;
            if (processed == 0)
            {
                break;
            }
        }

        return total;
    }

    public HttpRequestMessage Request(HttpMethod method, string uri, object? body = null, string? user = "alice", params string[] roles)
    {
        var request = new HttpRequestMessage(method, uri);
        if (user is not null)
        {
            request.Headers.Add(TestAuthHandler.UserHeader, user);
            if (roles.Length > 0)
            {
                request.Headers.Add(TestAuthHandler.RolesHeader, string.Join(',', roles));
            }
        }

        if (body is not null)
        {
            request.Content = System.Net.Http.Json.JsonContent.Create(body, body.GetType(), options: Json);
        }

        return request;
    }

    public static readonly JsonSerializerOptions Json = JsonSerializerOptions.Web;

    public static JsonObject? ToObject(object? data) => data switch
    {
        null => null,
        JsonObject obj => obj,
        string json => JsonNode.Parse(json)!.AsObject(),
        _ => JsonSerializer.SerializeToNode(data, data.GetType(), Json)!.AsObject(),
    };

    public async ValueTask DisposeAsync()
    {
        Client.Dispose();
        await App.StopAsync();
        await App.DisposeAsync();
        if (ownsConnection)
        {
            await Connection.DisposeAsync();
        }
    }
}

/// <summary>Authenticates requests carrying <c>X-Test-User</c>; roles from <c>X-Test-Roles</c> (comma separated).</summary>
public sealed class TestAuthHandler(IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder)
    : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    public const string SchemeName = "Test";
    public const string UserHeader = "X-Test-User";
    public const string RolesHeader = "X-Test-Roles";

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (!Request.Headers.TryGetValue(UserHeader, out var user) || string.IsNullOrEmpty(user))
        {
            return Task.FromResult(AuthenticateResult.NoResult());
        }

        var claims = new List<Claim> { new(ClaimTypes.Name, user!), new(ClaimTypes.NameIdentifier, user!) };
        if (Request.Headers.TryGetValue(RolesHeader, out var roles))
        {
            claims.AddRange(roles.ToString().Split(',', StringSplitOptions.RemoveEmptyEntries).Select(r => new Claim(ClaimTypes.Role, r)));
        }

        var principal = new ClaimsPrincipal(new ClaimsIdentity(claims, SchemeName));
        return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(principal, SchemeName)));
    }
}

/// <summary>Ordered log of task executions (<c>record</c> task) shared by the test.</summary>
public sealed class TaskLog
{
    private readonly ConcurrentQueue<string> entries = new();

    public IReadOnlyList<string> Entries => [.. entries];

    public void Add(string entry) => entries.Enqueue(entry);
}

/// <summary><c>record</c>: <c>{ "name": "a.entry" }</c> – appends the name (and optionally a data value) to <see cref="TaskLog"/>.</summary>
public sealed class RecordTask(TaskLog log) : IWorkflowTask
{
    public Task ExecuteAsync(WorkflowTaskContext context, CancellationToken cancellationToken)
    {
        log.Add(context.Config["name"]?.GetValue<string>() ?? context.Task.DisplayKey);
        if (context.Config["set"]?.GetValue<string>() is { } path)
        {
            JsonPath.Set(context.Data, path, JsonValue.Create(true));
        }

        return Task.CompletedTask;
    }
}

public sealed class FlakyCounter
{
    private readonly ConcurrentDictionary<string, int> calls = new(StringComparer.Ordinal);

    public int Increment(string key) => calls.AddOrUpdate(key, 1, (_, c) => c + 1);

    public int Calls(string key) => calls.GetValueOrDefault(key);
}

/// <summary><c>flaky</c>: <c>{ "failTimes": 2 }</c> – fails the first N calls (per task key), then succeeds.</summary>
public sealed class FlakyTask(FlakyCounter counter) : IWorkflowTask
{
    public Task ExecuteAsync(WorkflowTaskContext context, CancellationToken cancellationToken)
    {
        var call = counter.Increment(context.Task.DisplayKey);
        var failTimes = context.Config["failTimes"]?.GetValue<int>() ?? 1;
        if (call <= failTimes)
        {
            throw new WorkflowTaskException($"flaky failure {call}");
        }

        context.Data["flakyCalls"] = call;
        return Task.CompletedTask;
    }
}

/// <summary><c>fail</c>: always throws (also writes data first, to prove the rollback).</summary>
public sealed class FailTask(FlakyCounter counter) : IWorkflowTask
{
    public Task ExecuteAsync(WorkflowTaskContext context, CancellationToken cancellationToken)
    {
        counter.Increment(context.Task.DisplayKey);
        context.Data["dirty"] = true;
        throw new WorkflowTaskException(context.Config["message"]?.GetValue<string>() ?? "boom");
    }
}

/// <summary>Records events and the committed state of the instance at the moment the handler runs.</summary>
public sealed class RecordingEventHandler(IServiceScopeFactory scopeFactory) : IWorkflowEventHandler
{
    private readonly ConcurrentQueue<(WorkflowEvent Event, string? CommittedState, WorkflowInstanceStatus? CommittedStatus)> events = new();

    public bool Throw { get; set; }

    public IReadOnlyList<(WorkflowEvent Event, string? CommittedState, WorkflowInstanceStatus? CommittedStatus)> Received => [.. events];

    public IEnumerable<WorkflowEventType> TypesFor(Guid instanceId) => Received.Where(e => e.Event.InstanceId == instanceId).Select(e => e.Event.Type);

    public async Task HandleAsync(WorkflowEvent workflowEvent, CancellationToken cancellationToken)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<WorkflowDbContext>();
        var row = await db.Instances.AsNoTracking().FirstOrDefaultAsync(i => i.Id == workflowEvent.InstanceId, cancellationToken);
        events.Enqueue((workflowEvent, row?.State, row?.Status));
        if (Throw)
        {
            throw new InvalidOperationException("handler failure");
        }
    }
}

public sealed class FakePublisher : IWorkflowMessagePublisher
{
    public ConcurrentQueue<(string Type, JsonObject Payload)> Published { get; } = new();

    public Task PublishAsync(string messageType, JsonObject payload, CancellationToken cancellationToken)
    {
        Published.Enqueue((messageType, payload));
        return Task.CompletedTask;
    }
}

/// <summary>Records requests and answers with a configurable response.</summary>
public sealed class FakeHttpHandler : HttpMessageHandler
{
    public ConcurrentQueue<(HttpMethod Method, Uri Uri, string? Body, Dictionary<string, string> Headers)> Requests { get; } = new();

    public HttpStatusCode StatusCode { get; set; } = HttpStatusCode.OK;

    public string ResponseBody { get; set; } = """{"ok":true}""";

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var body = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
        var headers = request.Headers.ToDictionary(h => h.Key, h => string.Join(",", h.Value), StringComparer.OrdinalIgnoreCase);
        if (request.Content is not null)
        {
            foreach (var h in request.Content.Headers)
            {
                headers[h.Key] = string.Join(",", h.Value);
            }
        }

        Requests.Enqueue((request.Method, request.RequestUri!, body, headers));
        return new HttpResponseMessage(StatusCode) { Content = new StringContent(ResponseBody) };
    }
}
