# Middt Framework

Building blocks for line-of-business applications made of an **ASP.NET Core REST API** and a **Blazor Server UI**:
generic repositories with attribute-driven search, CRUD controllers, JWT token endpoints, typed API clients and
ready-made list/detail/CRUD pages.

Targets **.NET 10 (LTS)**.

## Packages

| Project | Purpose |
|---|---|
| `Middt.Framework.Model` | Wire contracts: `BaseResponseModel`/`BaseResponseDataModel<T>` (result envelope), `BaseSearchRequestModel<T>`, settings, `Query*` filter attributes. No dependencies. |
| `Middt.Framework.Data` | `IBaseRepository<T>` / `BaseCrudRepository<T, TContext>` on EF Core 10, `QueryFilterBuilder` (filter / sort / page). |
| `Middt.Framework.Common` | Typed API clients (`BaseCrudApiClient<T>`) on `IHttpClientFactory` + resilience, token client, SignalR hub/client, e-mail, Syncfusion document export. |
| `Middt.Framework.Api` | `AddMiddtApi()` / `UseMiddtApi()`, CRUD + token controllers, ProblemDetails, versioning, OpenAPI + Scalar, JWT, rate limiting, HybridCache. |
| `Middt.Framework.Blazor.Web` | `AddMiddtBlazor()` / `UseMiddtBlazor()`, Radzen based list/detail/CRUD pages, modals, authorization view, SignalR component, file upload, captcha. |
| `Middt.Framework.Workflow` | Embedded, JSON defined workflow (state machine) engine: manual/automatic/scheduled transitions, tasks, retries, timers, subflows, history – on EF Core, no Dapr. |
| `Middt.Framework.Plugin.Redis` | Redis connection, RedLock distributed lock, lock-protected read-through cache. |
| `Middt.Framework.Plugin.Rebus` | Messaging on [Rebus](https://github.com/rebus-org/Rebus/wiki) (MIT): `AddMiddtRebus()` with RabbitMQ or in-memory transport, publish/subscribe, send routing, retries and error queue. |

## Architecture

```
 Blazor Server (Middt.Framework.Blazor.Web)
   BaseRadzenListCrudPage<TService, TModel> ──► ICrudApiClient<TModel> (typed HttpClient + resilience + token refresh)
                                                        │  HTTPS / JSON, RFC 9457 ProblemDetails on errors
 ASP.NET Core API (Middt.Framework.Api)                 ▼
   BaseCrudController<TModel, TRepository> ──► IBaseRepository<TModel> ──► EF Core DbContext (DI scoped)
```

Design choices:

- **Dependency injection everywhere** – controllers receive repositories, repositories receive the `DbContext`; no service locator, no `new TContext()`.
- **Options pattern** – every settings class is bound with `IOptions<T>` (validated on start where it matters, e.g. `JwtSettings`).
- **Result envelope + ProblemDetails** – successful calls return `BaseResponseDataModel<T>`; failures are RFC 9457 ProblemDetails produced by a global `IExceptionHandler`. Exception details are only exposed in Development. The typed clients map both back to `Result = Error` so UI code keeps a single code path.
- **Async all the way** with `CancellationToken`, no `.Result` / `async void`.
- **Observability** – `ILogger<T>` with structured logging, OpenTelemetry via the sample `ServiceDefaults`, `/health` and `/alive` endpoints.

## Getting started

### API

```csharp
var builder = WebApplication.CreateBuilder(args);

builder.AddMiddtApi(o => o.Title = "My API");
builder.Services.AddDbContextPool<ShopContext>(o => o.UseSqlServer(builder.Configuration.GetConnectionString("Shop")));
builder.Services.AddScoped<ProductRepository>();

var app = builder.Build();
app.UseMiddtApi();
app.Run();
```

```csharp
public class ProductRepository(ShopContext context) : BaseCrudRepository<Product, ShopContext>(context);

[Route("api/v{version:apiVersion}/[controller]")]
[ApiVersion("1.0")]
public class ProductController(ProductRepository repository)
    : BaseCrudControllerWithAuth<Product, ProductRepository>(repository);
```

Endpoints: `GET getbyid?id=`, `GET getall`, `POST getitems`, `POST insert`, `POST update`, `POST delete`.
OpenAPI documents are served per version at `/openapi/v1.json`, the UI at `/scalar`.

Required configuration:

```jsonc
"JwtSettings": {
  "Secret": "<at least 32 characters – use user-secrets / environment variables, never source control>",
  "Issuer": "my-api",
  "Audience": "my-clients",
  "Expires": 600
}
```

### Search models

The search payload of `getitems` is the entity itself; attributes control the generated filter:

```csharp
public partial class ViewOrder : BaseRequestModel
{
    [NotMapped, QueryDate(DateSearchType.GreaterThanOrEqual, nameof(OrderDate))]
    public DateTime? OrderStartDate { get; set; }

    [QueryString(StringSearchType.StartsWith)]
    public string StoreName { get; set; }
}
```

Null / empty values and read-only (computed) properties are ignored, zero is ignored for numbers unless
`[QueryIsZero]` is present, and `SortProperty` is validated against the model's properties.

### Blazor Server

```csharp
builder.AddMiddtBlazor();                               // Radzen, Syncfusion, token storage, auth state, ApiSettings
builder.Services.AddMiddtApiClient<ProductService>();   // typed client with standard resilience handler

var app = builder.Build();
app.UseMiddtBlazor();
```

```csharp
public sealed class ProductService(HttpClient http, IBaseSessionState session, ITokenService tokens,
    IOptions<ApiSettings> settings, ILogger<ProductService> logger)
    : BaseCrudApiClient<Product>(http, session, tokens, settings, logger)
{
    protected override string ControllerName => "Product";
}
```

```razor
@page "/products"
@inherits BaseListCrudPageCode<ProductService, Product>

<BaseRadzenListCrudPage TService="ProductService" TModel="Product" ModalTitle="Product" @ref="baseListCrudPage">
    <ListContent>
        <RadzenDataGridColumn TItem="Product" Property="ProductName" Title="Name" />
    </ListContent>
    <EditContent>
        <RadzenTextBox @bind-Value="Model.ProductName" />
    </EditContent>
</BaseRadzenListCrudPage>
```

## Workflows

`Middt.Framework.Workflow` runs long-lived business processes defined in JSON (inspired by vNext, deliberately smaller and
without Dapr): one process, your database, a background job processor.

```csharp
builder.Services.AddMiddtWorkflow(o =>
{
    o.UseDbContext(db => db.UseSqlServer(connectionString)); // any EF Core relational provider
    o.AutoCreateSchema = true;                               // creates the "wf" tables (use migrations in production)
    o.AddDefinitionsFromDirectory("Workflows");              // *.json, validated at startup
    o.AddTask<NotifyCustomerTask>("notify-customer");        // application tasks (IWorkflowTask)
});
app.MapMiddtWorkflows("api/workflows");                     // REST API (authorized)
```

A definition ([full example](Sample/Middt.Sample.Api/Workflows/order-approval.json)):

```jsonc
{
  "key": "order-approval", "version": 1,
  "timeout": { "after": "P7D", "target": "expired" },
  "start": { "target": "routing",
             "input": { "schema": { "type": "object", "additionalProperties": false, "required": ["amount"],
                                    "properties": { "amount": { "type": "number", "minimum": 0 } } },
                        "set": { "requestedBy": "user.Name" } } },
  "states": [
    { "key": "routing", "transitions": [
        { "key": "needs-review", "target": "review", "trigger": "Automatic", "rule": "data.amount > 5000" },
        { "key": "auto-approve", "target": "approved", "trigger": "Automatic", "default": true } ] },
    { "key": "review", "subType": "Human", "transitions": [
        { "key": "approve", "target": "approved", "roles": ["manager"] },
        { "key": "escalate", "target": "escalated", "trigger": "Scheduled", "after": "PT24H" } ] },
    { "key": "escalated", "subType": "Human", "transitions": [ { "key": "approve", "target": "approved", "roles": ["director"] } ] },
    { "key": "approved", "type": "Finish", "subType": "Success",
      "onEntry": [ { "task": "publish", "config": { "type": "OrderApproved", "payload": { "id": "{{ instance.id }}" } } } ] },
    { "key": "expired", "type": "Finish", "subType": "Timeout" }
  ]
}
```

| Concept | Supported |
|---|---|
| States | `Initial`, `Intermediate`, `Finish`, `SubFlow`; sub types `Human`, `Success`, `Error`, `Cancelled`, `Timeout` |
| Transitions | `Manual` (API, `roles`), `Automatic` (`rule`, one `default` else-branch, chained), `Scheduled` (`after`), shared transitions (`availableIn`), `$self` (data update), workflow `timeout` |
| Input | JSON Schema subset (`type`, `properties`, `required`, `additionalProperties`, `enum`, `const`, min/max, lengths, `pattern`, `format`, `items`); the body is only written when a transition declares `input` (`into` path, computed `set` values) |
| Tasks | `onEntry`, `onExit`, `onExecute`; built-ins `set`, `log`, `http` (templated, host placeholders forbidden, values URL-encoded), `publish` (`IWorkflowMessagePublisher`, e.g. Rebus), `startWorkflow`; custom `IWorkflowTask`; `when` condition |
| Errors | per task / workflow `onError`: `maxAttempts` + exponential `delay`, then `Fault` (incident, `retry` endpoint), `Continue` or `Transition` (rolls the failed step back, then runs the given transition) |
| Subflows | `SubFlow` state starts a child (`input` mapping, `outputInto`); the parent continues when the child finishes (`child.subType` in rules); cancelling the parent cancels the child |
| Expressions | C#-like (DynamicExpresso) over `data`, `input`, `child`, `result`, `user` (`user.IsInRole("x")`), `instance`, `now`; reflection and type access are disabled; `{{ expr }}` templates in task config |
| Runtime | Busy flag via conditional UPDATE (concurrent requests → 409), one transaction per command, data snapshot + history per transition, incidents, idempotent start by business key, async transitions (202), durable timers with leases (multi-instance safe), crash recovery of Busy instances, `IWorkflowEventHandler` notifications after commit |

REST API (all responses use the `BaseResponseDataModel<T>` envelope; errors are ProblemDetails):
`GET /api/workflows`, `GET /{workflow}`, `POST /{workflow}/instances`, `GET /{workflow}/instances`,
`GET /{workflow}/instances/{id}` (includes the caller's available transitions and their input schemas),
`POST /{workflow}/instances/{id}/transitions/{key}[?async=true]`, `GET …/history`, `GET …/incidents`, `POST …/retry`.

Blazor: register `builder.Services.AddMiddtApiClient<WorkflowApiClient>();` and use the components from
`Middt.Framework.Blazor.Web.Base.Component.Workflow` – `WorkflowInstanceList` (paged list, start dialog generated from the
start input schema), `WorkflowInstanceDetail` (state, data, history, incidents, retry, one button per available transition
with a form generated from its input schema) and `JsonSchemaForm`. See `Sample/Middt.Sample.BlazorServer/Pages/Workflow/OrderApprovalPage.razor`
(menu "Sipariş Onayı").

Compared to vNext this engine intentionally leaves out the separate execution host, Dapr (jobs, locks, pub/sub, state),
inbox/outbox relays, Roslyn C# scripts, schema-per-flow storage, views/functions/extensions and long polling; tasks run
in-process and side effects should be idempotent (at-least-once on retries).

## Messaging (Rebus)

```csharp
builder.Services.AddMiddtRebus(builder.Configuration, rebus => rebus
    .Subscribe<CategoryCreated>()          // pub/sub (RabbitMQ topic exchange)
    .Map<CreateInvoice>("billing"));       // bus.Send(...) routes
builder.Services.AddMiddtRebusHandlersFromAssemblyOf<CategoryCreatedHandler>();

public sealed class CategoryCreatedHandler : IHandleMessages<CategoryCreated>
{
    public Task Handle(CategoryCreated message) => ...;
}

await bus.Publish(new CategoryCreated(id, name, now));   // inject Rebus.Bus.IBus
```

```jsonc
"RebusSettings": {
  "ConnectionString": "amqp://user:password@rabbitmq:5672", // or "UseInMemoryTransport": true for local runs/tests
  "InputQueueName": "my-service",
  "MaxDeliveryAttempts": 5,                                 // then moved to "ErrorQueueName"
  "ErrorQueueName": "error",
  "Routes": { "MyApp.Messages.CreateInvoice, MyApp.Messages": "billing" }
}
```

Messages are serialized with System.Text.Json (no type-name driven polymorphic deserialization). Any other Rebus feature
(sagas, timeouts, transactional outbox, second-level retries) can be enabled through `rebus.Configure(c => ...)`.
Publishing after `SaveChanges` is not atomic with the database write; use Rebus' outbox when that guarantee is required.

## Security

The framework follows the OWASP Top 10 / ASVS recommendations by default.

**Authentication (Blazor ↔ browser) – BFF pattern.** The browser only gets an `HttpOnly`, `SameSite=Lax`, `Secure`
(outside Development) session cookie `middt.auth` that carries the user's claims and a server side session id.
The API's access and refresh tokens are stored on the server (`IServerTokenStore`, encrypted with Data Protection,
in `IDistributedCache`), so no token is ever readable by JavaScript or stored in the browser. Login and logout are
server rendered Razor Pages (`/Account/Login`, `/Account/Logout`) with antiforgery tokens, per-IP rate limiting,
generic error messages and open-redirect protection (`LocalUrl`). Sessions have an idle and an absolute timeout,
are revalidated every minute by the circuit, and a replayed cookie is rejected once its server session is gone.
For more than one instance (or containers) register a shared `IDistributedCache` and persist the Data Protection keys
(e.g. Redis, see the sample).

**Tokens (Blazor ↔ API).** Short lived JWT access tokens (HS256, issuer/audience/lifetime validated, zero clock skew).
Refresh tokens are random 256-bit values, stored hashed, single use (rotation) and grouped in families with an absolute
lifetime; replaying a used refresh token revokes the whole family (OAuth 2.0 Security BCP). Logout revokes the family
via `POST token/revoke`. Implement `CheckUsernamePasswordAsync` with a salted password hash (e.g. ASP.NET Core Identity's
`PasswordHasher`) and `LoadUserAsync` so disabled users lose access on the next refresh. Keep `JwtSettings:Secret`
(≥ 32 chars) in a secret store / environment variable.

**Authorization & mass assignment.** `BaseCrudControllerWithAuth` requires an authenticated user. Updates load the stored
record and copy only editable scalar columns (`ApplyUpdate`): keys, navigations, store generated columns and
`[Editable(false)]` / `[ReadOnly(true)]` properties are never taken from the request; inserts drop navigation graphs and
client chosen identity values; deletes use only the key. Override `AuthorizeAsync` (record level, prevents IDOR) and
`ApplyAccessFilter` (row level for lists):

```csharp
public class OrderController(OrderRepository repository) : BaseCrudControllerWithAuth<Order, OrderRepository>(repository)
{
    protected override IQueryable<Order> ApplyAccessFilter(IQueryable<Order> query) => query.Where(o => o.OwnerId == UserId);

    protected override Task<bool> AuthorizeAsync(Order order, CrudOperation operation, CancellationToken ct) =>
        Task.FromResult(order.OwnerId == UserId);
}
```

**Hardening.** ProblemDetails without exception details outside Development; dynamic LINQ filters are parameterised and
sort columns are white-listed; security headers (CSP, `X-Frame-Options`, `X-Content-Type-Options`, `Referrer-Policy`,
`Permissions-Policy`, COOP) and HSTS; CORS requires explicit origins; client IPs come from `X-Forwarded-For` only for
trusted proxies listed in `ForwardedHeaders:KnownProxies/KnownNetworks`; audit logs for login success/failure and refresh
token reuse; personal data is kept out of logs; vulnerable transitive packages are pinned in `Directory.Packages.props`.

## Running the sample

The sample (`Sample/`) is a bike store: SQL Server (BikeStores), Redis, RabbitMQ (Rebus), the API and the Blazor UI. Inserting a category publishes a `CategoryCreated` event that the API's own handler consumes.

**With .NET Aspire (recommended)** – requires Docker or Podman:

```bash
dotnet run --project Sample/Middt.Sample.AppHost
```

The Aspire dashboard shows all resources, logs, traces and metrics. Set the SQL password once with
`dotnet user-secrets set Parameters:sql-password "<password>" --project Sample/Middt.Sample.AppHost`
(Aspire prompts for missing parameters otherwise).

**With Docker Compose:**

```bash
docker compose up --build
```

UI: <http://localhost:8080> (login page `/Account/Login`, demo user `test@test.com` / `test`) · API reference: <http://localhost:8180/scalar>.

## Development

```bash
dotnet build Middt.Framework.slnx
dotnet test --solution Middt.Framework.slnx
dotnet pack Middt.Framework.slnx -c Release -o artifacts
```

Package versions are managed centrally in `Directory.Packages.props`; shared build settings live in `Directory.Build.props`.

## Migrating from 6.x

Version 10 is a breaking release. The main changes for consumers:

| 6.x | 10.x |
|---|---|
| `BaseApiStartup` / `BaseBlazorStartup` subclasses | `builder.AddMiddtApi()` + `app.UseMiddtApi()` / `builder.AddMiddtBlazor()` + `app.UseMiddtBlazor()` |
| `IBaseConfiguration.Get<T>()`, `config/appsettings.{ENVIRONMENT}.json` | `IOptions<T>`; standard `appsettings.{ASPNETCORE_ENVIRONMENT}.json` |
| `IBaseLog`, `Log4Net`, `ConsoleLog`, `BrowserConsoleLog` | `ILogger<T>` |
| `FrameworkDependencyHelper` (service locator) | constructor injection |
| Controllers/repositories created with `new()` | primary-constructor injection: `ProductController(ProductRepository r) : BaseCrudController…(r)` |
| `BaseCrudRepository()` creating its own `DbContext` | `BaseCrudRepository<T, TContext>(TContext context)`; register the context with `AddDbContext*` |
| Repository methods `GetById`, `Insert`, `Save`, `Task<IQueryable>` | `GetByIdAsync`, `InsertAsync`, `SaveChangesAsync`, `IQueryable` returned directly; `Update`/`Delete` are synchronous |
| `BulkInsert/BulkUpdate/BulkDelete` (EFCore.BulkExtensions) | `InsertAsync(IEnumerable)`, `Update(IEnumerable)`, `ExecuteUpdateAsync`, `ExecuteDeleteAsync` (EF Core) |
| `FromSql(string, params object[])` | `FromSql(FormattableString)` (parameterised) |
| Errors returned as `200` with `ex.ToString()` in `MessageList` | ProblemDetails with proper status codes; no exception details outside Development |
| `BaseTokenController` sync abstract methods, app-managed refresh tokens | `CheckUsernamePasswordAsync` + `LoadUserAsync`; refresh tokens are rotated/revoked by the framework (`IRefreshTokenStore`); new `POST token/revoke`; inject `IJwtTokenGenerator`; override `CreateClaims` instead of `AddClaimList` |
| Token in browser local storage (`BaseProtectedLocalStorage`), Blazor login page calling the API | HttpOnly cookie + server side token store (BFF); framework Razor Pages `/Account/Login` and `/Account/Logout`; `SecuritySettings:LoginPageURL` defaults to `/Account/Login` |
| `BaseAuthorizeView` / `RedirectToLogin` as the app's guard | standard `AuthorizeRouteView` + `[Authorize]` (claims are available during prerendering); `BaseAuthorizeView` remains for role based fragments |
| Update/Delete applied the client payload as-is | Update copies editable scalar columns onto the stored record, Delete uses only the key; missing records → 404; hooks `AuthorizeAsync` / `ApplyAccessFilter` |
| CORS policy without origins allowed any origin | origins must be listed (`"*"` = any origin, without credentials); an empty list allows nothing |
| `BaseCrudRefit<TInterface, TModel>` + RestEase interfaces | `BaseCrudApiClient<TModel>` registered with `AddMiddtApiClient<T>()`; per-entity interfaces are no longer needed |
| Pages with three type parameters `<TService, TInterface, TModel>` | two: `<TService, TModel>` |
| `ExecuteMethod(Action)` | `ExecuteMethod(Func<Task>)` |
| `RadzenGrid` / `RadzenGridColumn` | `RadzenDataGrid` / `RadzenDataGridColumn` (Radzen.Blazor 12) with server side paging & sorting |
| `SearchMethod` / `GetMethod` strings (reflection) | `SearchFunc` / `GetFunc` delegates |
| Callbacks assigned from code (`page.OnAfterSearch = …`) | Step2 code-behind classes override `OnBeforeSearch`/`OnAfterSearch`…; markup users bind the `EventCallback` parameters |
| `ReditectToLogin` | `RedirectToLogin` |
| Swashbuckle (`/swagger`) | built-in OpenAPI (`/openapi/v1.json`) + Scalar (`/scalar`) |
| `IBaseCache.GetSetValue` | `IBaseCache.GetOrCreateAsync` (HybridCache; uses Redis as L2 when an `IDistributedCache` is registered) |
| `RedisSettings.RedisModelList` | `RedisSettings.ConnectionString` |
| `Middt.Framework.Plugin.RabbitMQ` / `Middt.Framework.Plugin.MassTransit` (`BaseMassTransitConnection`, producer/consumer bases) | `Middt.Framework.Plugin.Rebus`: `AddMiddtRebus(...)`, `IBus.Send/Publish`, `IHandleMessages<T>` handlers |
| `StringSearchType.Any` | `StringSearchType.Equal` |
| `ArrayMaxLength` / `ArrayMinLength` were exclusive and rejected `null` | inclusive bounds, `null` is valid (combine with `[Required]`) |

## License

GPL-2.0-only
