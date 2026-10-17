# Middt Framework

Building blocks for line-of-business applications made of an **ASP.NET Core REST API** and a replaceable frontend
(**Blazor Server** and **Angular** today, both over the same backend): generic repositories with attribute-driven
search, CRUD controllers, JWT token endpoints, a workflow engine, server driven UI (JSON layouts), typed API clients
and ready-made list/detail/CRUD pages. See [docs/architecture.md](docs/architecture.md) for the layering.

Targets **.NET 10 (LTS)**.

## Packages

| Layer | Project | Purpose |
|---|---|---|
| Contracts | `Middt.Framework.Model` | Wire contracts: `BaseResponseModel`/`BaseResponseDataModel<T>` (result envelope), `BaseSearchRequestModel<T>`, auth, workflow and SignalR DTOs, settings, `Query*` filter attributes. No dependencies. |
| Contracts | `Middt.Framework.Expressions` | The **portable expression language** (identical in .NET and TypeScript, verified by `spec/expressions.conformance.json`) and the richer DynamicExpresso evaluator used by workflow rules. |
| Contracts | `Middt.Framework.Ui.Abstractions` | Server driven UI contract: layout model, widget specs, validator, portable formats, JSON Schema generator. No UI technology. |
| Backend | `Middt.Framework.Common` | Server side helpers for API and frontend hosts: security headers, forwarded headers, SignalR hub base, e-mail, Syncfusion document export. |
| Backend | `Middt.Framework.Data` | `IBaseRepository<T>` / `BaseCrudRepository<T, TContext>` on EF Core 10, `QueryFilterBuilder` (filter / sort / page). |
| Backend | `Middt.Framework.Api` | `AddMiddtApi()` / `UseMiddtApi()`, CRUD + token controllers, ProblemDetails, versioning, OpenAPI + Scalar, JWT, rate limiting, HybridCache. |
| Backend | `Middt.Framework.Workflow` | Embedded, JSON defined workflow (state machine) engine: manual/automatic/scheduled transitions, tasks, retries, timers, subflows, history – on EF Core, no Dapr. |
| Backend | `Middt.Framework.Ui.Server` | Layout store on EF Core (`ui` schema, revisions, optimistic concurrency), seeding, `MapMiddtUi()` REST API (incl. widgets and JSON Schema), plain C# static HTML renderer. |
| Client SDK | `Middt.Framework.Client` | Typed API clients (`BaseCrudApiClient<T>`, `WorkflowApiClient`, `UiApiClient`, `UiDataClient`) on `IHttpClientFactory` + resilience, token refresh, SignalR client. |
| Frontends | `Middt.Framework.Bff` | Backend-for-frontend blocks for any browser frontend: HttpOnly cookie session, server side token store with refresh, Login/Logout pages, `GET /bff/user`, YARP proxy of `/api` + `/hubs` with the session's token and CSRF protection (`AddMiddtSpaBff()` / `UseMiddtSpaBff()`). |
| Blazor | `Middt.Framework.Blazor.Web` | The Blazor frontend: `AddMiddtBlazor()` / `UseMiddtBlazor()`, BFF cookie auth, Radzen based list/detail/CRUD pages, workflow screens, modals, SignalR, file upload, captcha; layout renderer under `Ui/` (`AddMiddtUi()`, display + interactive widgets, `UiPageHost`, `UiLayoutHost`, runtime `UiLayoutEditor`). |
| Blazor | `Middt.Framework.Workflow.Razor` | Razor workflow views (`[WorkflowView]`, `WorkflowViewBase`, `WorkflowTransitionViewBase`, `WorkflowViewCatalog`). |
| TypeScript | `sdk/client/typescript/ui-core` (`@middt/ui-core`) | Types, portable expressions, formats, layout/data/workflow API clients for non-.NET frontends. |
| Angular | `sdk/client/angular` (`@middt/angular`) | Angular 22 frontend library: BFF auth, layout renderer/widgets/editor, workflow list/detail/wizard, SignalR – see [sdk/client/angular/README.md](sdk/client/angular/README.md). Demo app: `demo/client/angular/app`. |
| Plugins | `Middt.Framework.Plugin.Redis` | Redis connection, RedLock distributed lock, lock-protected read-through cache. |
| Plugins | `Middt.Framework.Plugin.Rebus` | Messaging on [Rebus](https://github.com/rebus-org/Rebus/wiki) (MIT): `AddMiddtRebus()` with RabbitMQ or in-memory transport, publish/subscribe, send routing, retries and error queue. |

**Middt Identity** (`identity/Middt.Identity`, not a package): the OAuth 2.0 / OpenID Connect server of Middt
applications – one service, configured by appsettings / environment (database, issuer, certificate, clients, users).
Sign-in, registration, password reset and authenticator enrollment run as workflow journeys; account pages and the
`/admin` console are Blazor. See [docs/identity.md](docs/identity.md).

### Repository layout

The repository has two halves with the same shape: **`sdk/`** (the framework) and **`demo/`** (the bike store sample).
Each is split into `shared` (used by server and client), `server` and `client`, and `client` has one folder per
frontend technology.

```
sdk/                              the framework (NuGet / npm packages)
  shared/                         Model · Expressions · Ui.Abstractions         contracts, no UI technology
  server/                         Common · Data · Api · Workflow · Ui.Server    backend, no frontend dependencies
    plugins/                      Plugin.Redis · Plugin.Rebus
  client/
    dotnet/                       Client                                        .NET client SDK (typed API clients)
    typescript/ui-core/           @middt/ui-core                                TypeScript contract, expressions, API clients
    bff/                          Bff                                           backend-for-frontend host blocks (Blazor + Angular)
    blazor/                       Blazor.Web · Workflow.Razor                   Blazor frontend
    angular/                      @middt/angular                                Angular frontend (library workspace)
identity/Middt.Identity          the OAuth 2.0 / OpenID Connect server: one ready to run service (not an SDK package)
demo/                             the bike store sample
  shared/                         Sample.Common · Sample.Views · ServiceDefaults
  server/                         Sample.Api (+ Layouts/, Workflows/ seeds) · identity.json (demo settings of Middt Identity)
  client/
    blazor/                       Sample.BlazorServer
    angular/                      Sample.Angular (BFF host) · app/ (Angular demo app using @middt/angular as a package)
  Middt.Sample.AppHost            Aspire orchestration of everything
spec/                             expressions.conformance.json · ui-layout.schema.json (shared by .NET and TypeScript)
tests/  docs/  data/
```

`Middt.Framework.Workflow.Razor` stays a separate package on purpose: it only holds the Razor workflow view base
classes, which the shared view library *and the API* (static HTML of `view.component` states) reference – putting it into
`Blazor.Web` would drag Radzen and the BFF into the backend.

## Architecture

```
 Contracts      Model · Expressions (portable) · Ui.Abstractions · spec/*.json      ← shared by every layer and language
 Backend        Api · Data · Workflow · Ui.Server · Common                          ← no Razor/Blazor/Radzen
 Client SDK     Client (typed HttpClient + resilience + token refresh)
 Frontends      BFF: Bff   Blazor: Blazor.Web · Workflow.Razor   Angular: @middt/ui-core · @middt/angular

 Blazor Server   BaseRadzenListCrudPage<TService, TModel> / layout widgets ──► Middt.Framework.Client
                                                        │  HTTPS / JSON, RFC 9457 ProblemDetails on errors
 ASP.NET Core API                                       ▼
   BaseCrudController<TModel, TRepository> ──► IBaseRepository<TModel> ──► EF Core DbContext (DI scoped)
```

The backend never references a frontend package; a frontend talks to it only through REST/JSON and the shared
contracts. The Angular frontend was added exactly that way – a renderer on top of `@middt/ui-core` behind the same BFF – see
[docs/architecture.md](docs/architecture.md).

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
builder.AddMiddtBlazor();                               // Radzen, BFF session, auth state, ApiSettings (Syncfusion: o.UseSyncfusion = true)
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

A definition ([full example](demo/server/Middt.Sample.Api/Workflows/order-approval.json)):

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
| Transitions | `Manual` (API, `roles`, optional availability `rule`), `Automatic` (`rule`, one `default` else-branch, chained), `Scheduled` (`after`), shared transitions (`availableIn`), `$self` (data update), workflow `timeout` |
| Wizards | `step` numbers on states (progress indicator), `"view": { "inline": true }` on transitions (step form rendered inside the screen, pre-filled from `input.into` when a step is revisited), manual transitions with a `rule` (availability condition, e.g. one "Back" per branch); see [order-approval.v2.json](demo/server/Middt.Sample.Api/Workflows/order-approval.v2.json) |
| Input | JSON Schema subset (`type`, `properties`, `required`, `additionalProperties`, `enum`, `const`, min/max, lengths, `pattern`, `format`, `items`); the body is only written when the transition declares an `input.schema` (`into` path); `set` computes values (an input without schema ignores the body). UI hint `"x-lookup": { "entity": "Customer", "valueField": "customerId", "textField": "name" }` on a property: both frontends render a select whose options come from `api/v{version}/{entity}/getall` – capped at `MaxGetAllItems` (1000), so return the relevant rows first, e.g. newest first (shape checked at load time, the value itself is validated by the property's other keywords) |
| Tasks | `onEntry`, `onExit`, `onExecute`; built-ins `set`, `log`, `http` (templated, host placeholders forbidden, values URL-encoded), `publish` (`IWorkflowMessagePublisher`, e.g. Rebus), `startWorkflow`, `setKey` (gives the instance its business key once it is known, e.g. `category-{{ data.categoryId }}`; a start with that key then returns the instance); custom `IWorkflowTask`; `when` condition |
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
with a form generated from its input schema) and `JsonSchemaForm`. A schema property with
`"x-lookup": { "entity": "Customer", "valueField": "customerId", "textField": "email" }` is rendered as a select of the
entity's rows (same shape as the layout field `lookup`; the value type follows the property `type`). The demo has one
generic page per workflow, `/workflow/{key}` (`demo/client/blazor/Middt.Sample.BlazorServer/Pages/Workflow/WorkflowPage.razor`,
menu group "İş Akışları"); `?start=1` opens the start dialog once (`WorkflowInstanceList.OpenStart`), `key` prefills
its business key and every other parameter named like a start input property prefills that field (typed by the schema,
e.g. `/workflow/category-request?start=1&key=category-5&categoryId=5`); nothing starts before "Başlat", and these
parameters are removed from the URL once the dialog is open. A start with the business key of an existing instance
returns that instance (200) with `messageList` = `WorkflowEndpoints.ExistingInstanceMessage`; both frontends then show
"Mevcut akış açıldı" instead of "Akış başlatıldı".

### Server driven UI (Razor views)

States and transitions can name a Razor component that renders them; the definition decides which screen is shown,
the screens themselves stay compiled, type checked Razor (no runtime compilation, no view DSL):

```jsonc
{ "key": "manager-review", "view": { "component": "order-review" },
  "transitions": [ { "key": "reject", "target": "rejected", "view": { "component": "reject-reason" }, "input": { … } } ] }
```

```razor
@attribute [WorkflowView("order-review")]   @* state view: WorkflowViewBase (Instance, Data, Text("path")) *@
@inherits WorkflowViewBase
<h3>Order #@Text("orderId") · @Text("customerName")</h3>
```

- `WorkflowViewBase` (state screens) and `WorkflowTransitionViewBase` (input forms: call `SubmitAsync(body)`; API
  validation errors arrive in `Errors`) live in `Middt.Framework.Workflow.Razor`. Razor views are a Blazor feature;
  states that must work in every frontend use JSON layouts (`view.layout`).
- Blazor: `builder.Services.AddMiddtWorkflowViews(WorkflowViewCatalog.FromAssemblyOf<OrderReviewView>())` –
  `WorkflowInstanceDetail` renders the state view with `DynamicComponent` (raw data stays available under "Ham veri")
  and uses the transition view instead of the schema generated form. Without a registered view it falls back to the
  generic UI.
- API: `o.UseViews(WorkflowViewCatalog.FromAssemblyOf<OrderReviewView>().Views)` validates view names at startup and
  enables `GET /api/workflows/{workflow}/instances/{id}/view`, which renders the state view to static HTML
  (`HtmlRenderer`) for non-Blazor clients, e-mails or PDFs. The endpoint asks every registered
  `IWorkflowViewRenderer` (Razor components, JSON layouts, your own) and returns the first result. Keep views free of JS interop and never output instance
  data with `MarkupString` – Razor encodes everything else.
- Sample: `demo/shared/Middt.Sample.Views` (`order-review`, `order-result`, `reject-reason`) is shared by the API and the UI.

Compared to vNext this engine intentionally leaves out the separate execution host, Dapr (jobs, locks, pub/sub, state),
inbox/outbox relays, Roslyn C# scripts, schema-per-flow storage, views/functions/extensions and long polling; tasks run
in-process and side effects should be idempotent (at-least-once on retries).

## Server driven UI (JSON layouts)

Screens can also be data: a **layout** is a JSON tree of registered **widgets**, stored in the database and editable at
runtime – no deployment for a new list page, a changed column or a new workflow state screen. Layouts never contain
code or markup: only widget names, properties and sandboxed expressions, validated against the widget catalog.
A layout is frontend independent: Blazor renders it today (`Middt.Framework.Blazor.Web`, namespace `Middt.Framework.Blazor.Web.Ui`), the API renders it to static
HTML (`UiHtmlRenderer`), and Angular renders it with `@middt/angular` (on `@middt/ui-core`).

```jsonc
{ "key": "bike-category", "title": "Kategoriler", "kind": "page", "version": 1,
  "menu": { "title": "Category", "icon": "category", "order": 10 }, "roles": [],
  "body": [
    { "widget": "heading", "text": "Kategoriler" },
    { "widget": "grid", "entity": "Category",                       // api/v1/Category/getitems|insert|update|delete
      "columns": [ { "field": "categoryName", "title": "Ad" } ],
      "search":  [ { "field": "categoryName", "label": "Ad" } ],
      "create": true, "edit": true, "delete": true,
      "form":    [ { "field": "categoryName", "label": "Ad", "required": true, "maxLength": 255 } ],
      "rowActions": [ { "text": "Ürünler", "icon": "search", "navigate": "/ui/bike-category-products?id={{ row.categoryId }}" } ] } ] }
```

| Widget | Purpose |
|---|---|
| `heading`, `text`, `alert`, `badge`, `divider`, `link` | Display; text properties are `{{ }}` templates |
| `fields` | Label/value list with formats (`N2`, `d`, `g`) |
| `section`, `columns`, `list` | Containers (`list` repeats its children per array item as `row`) |
| `grid` *(interactive)* | Server paged/sorted grid of a CRUD controller: search fields, fixed `filter`, row actions, create/edit/delete dialog |
| `record` *(interactive)* | Loads one entity by `recordId` and shows its children with it as `record` |

- Every node accepts `visibleWhen` (e.g. `user.IsInRole("manager")`, `data.amount > 20000`). Variables: `data`,
  `query`, `row`, `record`, `instance`, `user`, `now`. Expressions use the **portable language**: member access with
  null propagation, `! - * / % + < <= > >= == != && || ?? ?:`, text methods (`Contains`, `StartsWith`, `ToUpper`, …),
  `Math.Round/Abs/Floor/Ceiling/Min/Max`; `==` is strict (`query.id == "42"`, not `42`). Unknown variables or methods
  are rejected when the layout is saved. Formats: `N0`–`N6`, `F0`–`F6`, `d`, `D`, `g`, `t`. Form/search field types: `text`, `textarea`, `number`,
  `integer`, `date`, `datetime`, `checkbox`, `select` (static `options` or a `lookup` entity) and `picker` (search
  dialog over a lookup entity).
- **API**: `builder.Services.AddMiddtUi(o => o.UseDbContext(...).AddLayoutsFromDirectory("Layouts"))` and
  `app.MapMiddtUi()`. Seed files are validated at startup (a broken file stops the app) and overwrite stored layouts
  only when their `version` is higher, so runtime edits survive restarts. Reads are open to authenticated users
  (filtered by the layout's `roles`); `PUT/DELETE/validate/revisions` need `MiddtUiOptions.AdminRole` (`ui-admin`).
  `PUT` carries the version it is based on (0 = new) – a stale save gets 409; every change keeps a revision.
- **Blazor**: `builder.Services.AddMiddtUi()`, a page `@page "/ui/{Key}"` with `<UiPageHost Key="@Key" />` and an
  admin page with `<UiLayoutEditor />` (edit JSON, validate on the server, preview, save, revisions, delete).
  Grid/record widgets call the CRUD controllers **with the signed-in user's token**, so API authorization is unchanged;
  a layout can only show what the user may already read.
- **Workflows**: a state can use `"view": { "layout": "order-escalated" }` instead of a Razor component;
  `WorkflowInstanceDetail` renders it with `data` and `instance`. For `GET …/instances/{id}/view` register an
  `IWorkflowViewRenderer` (the sample's `WorkflowLayoutRenderer` uses `UiLayoutStore` + `UiHtmlRenderer`).
- **Editors**: `GET api/ui/schema` (also committed as `spec/ui-layout.schema.json`) is the JSON Schema of layouts;
  seed files reference it with `"$schema"` for completion in VS Code / Rider.
- **Custom widgets**: register the spec in the API – `o.AddWidget(spec, html)` (optional static HTML) – and an
  implementation in each frontend: `AddMiddtUi(w => w.Add<MyWidget>(spec))` in Blazor (component deriving from
  `UiWidgetBase`), `provideMiddtUiWidgets({ name, component })` in Angular.
- Razor views (`[WorkflowView]`) remain the choice for complex, bespoke screens; layouts cover the many list / detail /
  form / status screens that only need configuration.

The sample's Category, Category → Products, Product and Order screens are layouts
(`demo/server/Middt.Sample.Api/Layouts`), reachable from the menu, which lists every page layout with a `menu` entry.
Old `/Bike/...` URLs redirect.

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

### Secure defaults (10.x review)

| Area | Default | Opt out / configure |
|---|---|---|
| OpenAPI + Scalar | Development only | `MiddtApiOptions.EnableOpenApi = true` |
| Page size | `getitems` clamped to 500, `getall` capped at 1000 | `MaxPageSize`, `MaxGetAllItems` |
| Search filters | values sent as SQL parameters (no literals, stable plans) | – |
| Workflow errors | raw task errors hidden from API callers (logged + stored) | `MiddtWorkflowOptions.ExposeErrorDetails` |
| Workflow instances | everyone allowed by `roles` sees all instances | `"instanceAccess": { "mode": "owner", "supervisorRoles": ["manager"] }` (the demo uses it) |
| Workflow `http` task | no redirects; any host of the definition | `AllowedHttpHosts` |
| BFF proxy | `/api/**/token/**` is not proxied (tokens never reach JS) | `MiddtBffOptions.ProxyDeniedPaths` |
| BFF login | per IP and per user name (failed logins); OIDC mode: password form disabled, code + PKCE at the provider | `LoginAttemptsPerMinute`, `LoginAttemptsPerUsernamePerMinute`, `Bff:Oidc` |
| Logout | auto-submits only from the same origin (no logout CSRF) | – |
| SPA shell | `*.html` only for signed-in users; no `Server` header | – |
| Compression | Brotli/Gzip for API, BFF and Blazor responses (login pages excluded: BREACH) | `EnableResponseCompression` / `ResponseCompression` / `UseResponseCompression` |
| .NET client | retries only safe methods, never 429, no retry for the token client | – |
| docker compose | secrets from `.env` (no defaults), DB/broker/API ports on localhost only, API trusts only the BFF containers' IPs for `X-Forwarded-For` | copy `.env.example` to `.env` |


The framework follows the OWASP Top 10 / ASVS recommendations by default.

**Authentication (browser ↔ frontend host) – BFF pattern (`Middt.Framework.Bff`, used by Blazor and Angular).** The
browser only gets an `HttpOnly`, `SameSite=Lax`, `Secure` (outside Development) session cookie (`middt.auth`, name
configurable per app) that carries the user's claims and a server side session id.
The API's access and refresh tokens are stored on the server (`IServerTokenStore`, encrypted with Data Protection,
in `IDistributedCache`), so no token is ever readable by JavaScript or stored in the browser. Login and logout are
server rendered Razor Pages (`/Account/Login`, `/Account/Logout`) with antiforgery tokens, per-IP rate limiting,
generic error messages and open-redirect protection (`LocalUrl`). Sessions have an idle and an absolute timeout,
are revalidated every minute by the circuit, and a replayed cookie is rejected once its server session is gone.
For more than one instance (or containers) register a shared `IDistributedCache` and persist the Data Protection keys
(e.g. Redis, see the sample). A signed-in user without a required role gets `/Account/AccessDenied`
(`SecuritySettings:AccessDeniedPageURL`; Blazor's `RedirectToLogin` shows "Erişim engellendi", Angular's
`middtRoleGuard` navigates there) – never the login page, which would loop with an identity provider (silent sign-in).

**Sign-in at an identity provider (OIDC mode).** Set `Bff:Oidc:Authority`, `ClientId` and `ClientSecret`
(`MiddtBffOptions.Oidc`; env vars `Bff__Oidc__Authority`, `Bff__Oidc__ClientId`, `Bff__Oidc__ClientSecret`, optional
`Bff__Oidc__BackchannelAuthority`, `Bff__Oidc__Scopes`, `Bff__Oidc__RequireHttpsMetadata`, `Bff__Oidc__CallbackPath`,
`Bff__Oidc__SignedOutCallbackPath`) and `/Account/Login` redirects to the provider instead of showing the password form
(the form post answers 404): authorization code flow with PKCE, `response_mode=query` (works with `SameSite=Lax` over
plain http), confidential client. The callback (`/signin-oidc`) creates the same server side session as the password
login – fresh session id, access/refresh/identity token in `IServerTokenStore`, a cookie with only `ClaimTypes.Name`,
`Role`, `NameIdentifier`, `Email` and the session id – and ends a previous session of the browser. Token refresh
(`BffTokenAccessor`, Blazor's session state) and revocation go to the provider's token/revocation endpoints
(`OidcTokenService`). `POST /Account/Logout` revokes the refresh token, ends the local session and navigates to the
provider's end-session endpoint (`id_token_hint`, back via `/signout-callback-oidc` to `/`). Server-to-server calls can go
to an internal URL (`BackchannelAuthority`, `BackchannelRewriteHandler`) while issuer and browser redirects keep the
public one. Without `Bff:Oidc:Authority` everything works as before (password form → API token controller).

**SPA (Angular) calls.** The Angular app never sees a token: it calls `/api/**` and `/hubs/**` on its own origin and the
BFF forwards them to the API (YARP) with the session's access token, refreshing it shortly before it expires (single
flight per session; a failed refresh ends the session → 401 → login page). Browser cookies are not forwarded. CSRF: API
calls must carry the `X-CSRF` header (cannot be set cross-site without a CORS preflight) and WebSocket upgrades must have a
same-origin `Origin`. API style paths answer 401/403 instead of redirects; page navigations go to the login page. The SPA
gets a strict CSP (`script-src 'self'`, no inline scripts).

**Tokens (Blazor ↔ API).** Short lived JWT access tokens (HS256, issuer/audience/lifetime validated, zero clock skew).
Refresh tokens are random 256-bit values, stored hashed, single use (rotation) and grouped in families with an absolute
lifetime; replaying a used refresh token revokes the whole family (OAuth 2.0 Security BCP). Logout revokes the family
via `POST token/revoke`. Implement `CheckUsernamePasswordAsync` with a salted password hash (e.g. ASP.NET Core Identity's
`PasswordHasher`) and `LoadUserAsync` so disabled users lose access on the next refresh. Keep `JwtSettings:Secret`
(≥ 32 chars) in a secret store / environment variable.

**Tokens of an identity provider (API authority mode).** With `Authentication:Authority` (or
`MiddtApiOptions.Authority`; env var `Authentication__Authority`) the API validates RS256 access tokens of that OpenID
Connect provider instead of its own HS256 tokens: signing keys from discovery, issuer = the authority, audience
`Authentication:Audience` (default `middt-api`), `Authentication:RequireHttpsMetadata` (default: outside Development),
discovery/JWKS through `Authentication:BackchannelAuthority` when set. OIDC claims are mapped to the claim types the
framework reads (`name` → `ClaimTypes.Name`, every `role` → `ClaimTypes.Role`, `sub` → `ClaimTypes.NameIdentifier` and
`ClaimTypes.Sid`, `email` → `ClaimTypes.Email`); SignalR's `access_token` query parameter keeps working under
`HubPathPrefix`. `JwtSettings` are optional in this mode (only needed if the app still hosts a `BaseTokenController`).

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

The demo (`demo/`) is a bike store: SQL Server (BikeStores), Redis, RabbitMQ (Rebus), the OAuth 2.0 / OpenID Connect identity provider, the API, the Blazor UI and the Angular UI. Both UIs sign in at the identity provider (authorization code + PKCE through their BFF) and the API accepts its access tokens. Creating a category (category-request workflow) publishes a `CategoryCreated` event that the API's own handler consumes.

**With .NET Aspire (recommended)** – requires Docker or Podman:

```bash
dotnet run --project demo/Middt.Sample.AppHost
```

The Aspire dashboard shows all resources, logs, traces and metrics. Set the SQL password once with
`dotnet user-secrets set Parameters:sql-password "<password>" --project demo/Middt.Sample.AppHost`
(Aspire prompts for missing parameters otherwise).

**With Docker Compose:**

```bash
cp .env.example .env    # then set IDP_BLAZOR_CLIENT_SECRET, IDP_ANGULAR_CLIENT_SECRET, MSSQL_SA_PASSWORD, RABBITMQ_DEFAULT_USER / _PASS
docker compose up --build
```

Blazor UI: <http://localhost:8080> · Angular UI: <http://localhost:8081> (same screens, layouts and workflows) ·
identity provider: <http://localhost:8083> (login, registration, password reset, authenticator, `/admin` console,
`/dev/mailbox` for verification codes) · demo users `test@test.com` / `test` (roles `manager`, `ui-admin`, `idp-admin`)
and `user@test.com` / `test` · layout editor: `/ui-admin` · API reference: <http://localhost:8180/scalar>.
Signing in once at the IdP signs you into both UIs (single sign-on); see [docs/identity.md](docs/identity.md).

**Linked workflows of the sample.** Every "new record" of the bike store goes through a workflow that writes the
real BikeStores tables with application tasks (`bike-*`, `demo/server/Middt.Sample.Api/Tasks`). In logical order:

| # | Workflow | Steps | Writes / links |
|---|---|---|---|
| 1 | `store-opening` v2 Mağaza açılışı | details → manager approval → lifecycle | `stores`; starts `staff-onboarding` with the new store pre-selected |
| 2 | `staff-onboarding` v2 Personel alımı | store → person (+ manager) → manager approval → lifecycle | `staffs` |
| 3 | `category-request` v2 Kategori talebi | name + justification → manager approval → lifecycle | `categories` (+ `CategoryCreated` event) |
| 4 | `product-launch` v2 Ürün lansmanı | category, brand, price, optional initial stock → price approval above 5.000 TL → lifecycle | `products`; starts `stock-replenishment` for the initial stock |
| 5 | `stock-replenishment` Stok ikmali | store, product, quantity → manager approval (automatic after 1 minute, immediate for orders) | `stocks` (+quantity) |
| 6 | `customer-onboarding` v2 Müşteri kaydı | Bireysel/Kurumsal → TCKN or tax number → contact → verification → lifecycle | `customers` |
| 7 | `order-approval` v3 Sipariş onayı | registered customer → store, product, quantity → credit check → finance/manager/express review | `orders` + `order_items` (status 1); starts `order-fulfillment` |
| 8 | `order-fulfillment` Sipariş karşılama | stock check (missing stock → `stock-replenishment` subflow) → reserve → picking → shipping → delivered | `stocks` (−quantity), order status 2 → 4, shipped date; cancel returns the stock (status 3) |

Rule violations a user can fix (duplicate e-mail, store without staff, not enough stock) do not fault the instance:
the task writes the Turkish message to `data.error` and the workflow moves to a "Düzeltme gerekli" step
(layout `bike-task-error`) that leads back to the form.

**One lifecycle per record.** Stores, staff, categories, products and customers change only through their workflow
(1–4, 6): their API `insert` / `update` / `delete` answer 403 ("Bu kayıt iş akışı üzerinden değiştirilir."), orders
(`ViewOrder`) and stocks are read-only too. After the record is created the instance does not finish: `setKey` gives it
the business key `store-12`, `staff-3`, `category-8`, `product-41` or `customer-1501` and it waits in **Aktif**
(`active`, current values in `data.store` / `data.staff` / …):

- `request-change` "Değişiklik talebi" (inline form prefilled with the current values) → `change-approval` (manager:
  `approve-change` → `bike-update-*` → back to `active` with the new values; `reject-change` / `withdraw-change` → `active`
  unchanged; a rule violation → `change-fix` "Düzeltme gerekli" → `back-to-record`).
- `request-close` "Kapatma talebi" (reason) → `close-approval` (manager `approve-close` / `reject-close`) →
  `bike-set-active` false → final state `closed` "Kapatıldı". Closing is a **soft delete** (`active = 0`): existing
  orders keep the record, lookups (`getall`, the selects of forms and filters) offer active records only, lists
  (`getitems`) show all with a "Durum" column and filter; new orders, staff of a closed store and products of a closed
  category are refused with a "… pasif durumda." message. A category with active products cannot be closed.
- Existing records (the seed has no instances) are **adopted**: the lists' row action "Değişiklik / kapatma" opens
  `/workflow/<key>?start=1&key=<entity>-<id>&<idField>=<id>`; the start input carries the id (`storeId`, `staffId`,
  `categoryId`, `productId`, `customerId`), the record is loaded and the instance goes straight to `active` – or to
  `inactive` "Pasif" for a passive record (`request-reactivate` → `reactivate-approval` → active). Starting again with
  the same key returns the existing instance.

The stores/categories/products/customers `active` columns are added at API startup (`BikeStoresSchemaUpgrade`,
idempotent, SQL Server).

## Development

**AI-assisted development:** [`AGENTS.md`](AGENTS.md) (map, commands, conventions, definition of done) plus nested
`AGENTS.md` files per area and task playbooks in [`docs/ai/`](docs/ai/README.md). Tool entry points: `CLAUDE.md`
(Claude Code, slash commands `/verify`, `/add-entity`, `/add-widget`, `/add-workflow`), `.github/copilot-instructions.md`,
`.cursor/rules/middt.mdc`.

```bash
dotnet build Middt.Framework.slnx
dotnet test --solution Middt.Framework.slnx
dotnet pack Middt.Framework.slnx -c Release -o artifacts
(cd sdk/client/typescript/ui-core && node --test test/)     # TypeScript core, same conformance suite
(cd sdk/client/typescript/ui-core && npm ci && npm run build)            # @middt/ui-core package
(cd sdk/client/angular && npm ci && npx ng build middt-angular)           # @middt/angular package
(cd demo/client/angular/app && npm ci && npx ng build sample)             # Angular demo app (consumes both packages)
(cd tests/e2e && npm ci && npx playwright install chromium && npm test)    # full-stack browser + SQL Server smoke tests (Compose running)
```

`spec/ui-layout.schema.json` is generated from the widget catalog; after changing widget specs regenerate it with
`MIDDT_UPDATE_SPEC=1 dotnet test --project tests/Middt.Framework.Tests` (a test fails while it is out of date).

Package versions are managed centrally in `Directory.Packages.props`; shared build settings live in `Directory.Build.props`.

## Upgrade notes (within 10.x)

- Syncfusion is opt-in in Blazor (`AddMiddtBlazor(o => o.UseSyncfusion = true)` or `AddMiddtSyncfusion`); pages using
  `BaseFileUploadComponent` must include `_content/Syncfusion.Blazor.Themes/bootstrap5.css` and
  `_content/Syncfusion.Blazor.Core/scripts/syncfusion-blazor.min.js`.
- Blazor host pages should keep `<persist-component-state />` and `~/` asset URLs (fingerprinted static assets).
- Custom layout widgets skip updates whose `Node` and `Context` are unchanged; override `SkipUnchangedParameters` if a
  widget renders state from elsewhere. Pass the same `Variables` dictionary to `UiLayoutHost` while nothing changed.
- Workflow commands answer 409 when the instance changed between validation and claim; workflow ids on SQL Server are
  SQL-Server-sequential GUIDs; new indexes are created at startup with `AutoCreateSchema`, otherwise via migration.
- `LocalUrl` and `UiUrl` reject control characters, non-ASCII (LocalUrl) and leading/trailing whitespace – percent-encode.
- Signing in again ends the previous server session; the .NET client no longer retries POST/PUT/PATCH/DELETE or 429.
- Workflow owner checks compare `StartedBy` with the user name – user names must be unique.
- `BaseSignalRHub` lets any signed-in user broadcast and join any group; override `Send`/`JoinGroup` to authorize targets.

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
| `Middt.Framework.Common` typed clients, token service, SignalR client (`Middt.Framework.Common.Http/Security/SignalR`) | `Middt.Framework.Client` (`Middt.Framework.Client.Http/Security/SignalR`, `AddMiddtApiClient(s)` in `Middt.Framework.Client.DependencyInjection`); SignalR DTOs in `Middt.Framework.Model.SignalR` |
| `BaseTokenHelper`, `INotification` in Common | `Middt.Framework.Blazor.Web.Security.BaseTokenHelper`, `Middt.Framework.Blazor.Web.Base.Notification.INotification` |
| `ArrayMaxLength` / `ArrayMinLength` were exclusive and rejected `null` | inclusive bounds, `null` is valid (combine with `[Required]`) |

## License

GPL-2.0-only
