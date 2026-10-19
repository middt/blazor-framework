# Architecture: contracts, backend, frontends

Middt is split so that the **frontend technology is replaceable**. Two frontends run side by side against the same API,
workflows and layouts: Blazor Server (`sdk/client/blazor`) and Angular (`sdk/client/angular`). Both sit behind the same
backend-for-frontend blocks (`sdk/client/bff`), so no frontend ever holds an API token.

```
                         ┌────────────────────────────── 1. Contracts (no UI technology) ──────────────────────────────┐
                         │ Middt.Framework.Model            wire DTOs: envelope, search, auth, workflow, SignalR          │
                         │ Middt.Framework.Expressions      portable expression language (+ DynamicExpresso for workflows)│
                         │ Middt.Framework.Ui.Abstractions  layout model, widget specs, validator, formats, JSON Schema   │
                         │ spec/                            expressions.conformance.json, ui-layout.schema.json           │
                         └───────────────▲───────────────────────────────▲──────────────────────────────▲───────────────┘
                                         │                               │                              │ same rules
┌──────────── 2. Backend ────────────────┴───┐   ┌──── 3. .NET client SDK ──┴──────┐   ┌──── TypeScript ───┴────────────┐
│ Api, Data, Common (server helpers)          │   │ Middt.Framework.Client           │   │ sdk/client/typescript/ui-core     │
│ Workflow   (engine; IWorkflowViewRenderer)  │   │ typed CRUD/workflow/layout       │   │ @middt/ui-core: types,         │
│ Ui.Server  (layout store, REST, schema,     │   │ clients, token refresh, SignalR  │   │ expressions, formats, clients  │
│            plain C# static HTML renderer)   │   └───────────────▲──────────────────┘   └───────────────▲────────────────┘
│ Plugins: Redis, Rebus, SyncfusionExport     │                   │                                      │
└─────────────────────▲───────────────────────┘   ┌──── 4. Blazor frontend ┴──────────┐   ┌──── Angular frontend ─────────┴┐
                      │  HTTPS / JSON (REST,      │ Blazor.Web  BFF auth, pages,      │   │ @middt/angular: widgets as     │
                      │  ProblemDetails)          │             workflow screens      │   │ Angular components, BFF/cookie │
                      └───────────────────────────│   layout widgets + editor (Ui/)   │   │ auth, workflow screens         │
                                                  │ Workflow.Razor  Razor state views │   └────────────────────────────────┘
                                                  └───────────────────────────────────┘
```

## Dependency rules

| Layer | May reference | Must not reference |
|---|---|---|
| Contracts | BCL, each other (Model ← Expressions ← Ui.Abstractions) | ASP.NET Core UI, Razor, Radzen, EF Core |
| Backend | Contracts, EF Core, ASP.NET Core | Client, Blazor.Web, Workflow.Razor (*) |
| Client SDK | Contracts | Backend, any UI technology |
| Blazor frontend | Contracts, Client, Common (host helpers) | Backend engine packages (Workflow, Ui.Server, Data, Api) |

(*) The only Razor in a backend process is opt-in: an API may reference a Razor view library to render
`view.component` workflow states to HTML (`MiddtWorkflowOptions.UseViews`). Frontend independent screens use layouts.

## What is the contract between backend and frontends?

1. **REST + JSON** – every API returns `BaseResponseDataModel<T>` (`result`, `messageList`, `count`, `data`); errors are
   RFC 9457 ProblemDetails. CRUD: `api/v{version}/{entity}/getitems|getall|getbyid|insert|update|delete`; workflows:
   `api/workflows/...`; layouts: `api/ui/...` (incl. `GET api/ui/widgets`, `GET api/ui/schema`).
2. **Layouts** – JSON documents validated against `spec/ui-layout.schema.json` (generated from the widget catalog)
   and the server validator. A layout names widgets; every frontend has its own implementation of each widget name.
3. **Portable expressions** – `visibleWhen`, `{{ }}` templates, list sources and filters use a small sandboxed language
   with identical semantics in .NET (`Middt.Framework.Expressions.Portable`) and TypeScript (`@middt/ui-core`).
   `spec/expressions.conformance.json` is executed by both test suites, so they cannot drift apart. Workflow rules and
   mappings run only on the server and keep the richer C# (DynamicExpresso) dialect.
4. **Formats and CSS** – value formats are limited to a portable set (`N0`–`N6`, `F0`–`F6`, `d`, `D`, `g`, `t`);
   display widgets share the `middt-ui-*` CSS classes (`sdk/client/blazor/Middt.Framework.Blazor.Web/wwwroot/middt-ui.css`).

## Renderers of a layout

| Renderer | Where | Widgets |
|---|---|---|
| Blazor (interactive) | `Middt.Framework.Blazor.Web` (`Ui/`) | display widgets + `grid`/`record`/`layout` (Radzen) |
| Static HTML | `Middt.Framework.Ui.Server` (`UiHtmlRenderer`, plain C#) | display widgets; interactive ones render a placeholder |
| Angular | `@middt/angular` on top of `@middt/ui-core` | same names, Angular components (Material) |

Custom widgets are registered in each place: the **spec** in the API (`MiddtUiOptions.AddWidget(spec, html?)`) and
in every frontend (`AddMiddtUi(w => w.Add<MyWidget>(spec))` in Blazor, a component map in Angular). Reusable composite
widgets can also be defined in pure JSON (`kind: "view"`) and embedded via `{ "widget": "layout", "key": "<layout-key>", "data": "<optional-expr>" }` without writing any C# or TypeScript code (see [`docs/ai/backend-driven-json-ui.md`](ai/backend-driven-json-ui.md)).

## How the Angular frontend was added (and how to add another)

1. **Workspace**: `sdk/client/angular` (Angular 22 + Material) with the library `@middt/angular` (demo app in `demo/client/angular/app`); it
   consumes `@middt/ui-core` as a `file:` package.
2. **Authentication**: the SPA is served by a BFF host (`demo/client/angular/Middt.Sample.Angular`: `AddMiddtSpaBff()` +
   `UseMiddtSpaBff()`). It owns the cookie session and the tokens, serves the server rendered login/logout pages,
   `GET /bff/user`, and proxies `/api/**` and `/hubs/**` (YARP, WebSockets included) to the API with the session's
   access token, refreshing it before it expires. CSRF: API calls carry an `X-CSRF` header, WebSocket upgrades must be
   same-origin. The API is unchanged.
3. **Layouts**: `UiLayoutRendererComponent` loads layouts and maps widget names to Angular
   components (`MIDDT_UI_WIDGETS`); `@middt/ui-core` evaluates `visibleWhen`, templates and
   formats exactly like .NET; the same CSS classes (`middt-ui-*`) and E2E selectors work in both frontends.
   The Angular package has focused `core`, `ui`, `workflow`, and `signalr` entry points; the root entry point remains
   an aggregate for compatibility.
4. **Workflows**: `WorkflowApiClient` (ui-core) + list/detail/wizard components; all demo workflow states use `view.layout`
   JSON layouts (`kyc-review`, `credit-review`, `order-review`, `order-result`, `order-escalated`, `risk-detail`) rendered by
   the layout host in both frontends (the SDK also supports optional `view.component` registrations via `provideWorkflowViews()`);
   unregistered views fall back to the server rendered HTML.
   Transition input schemas are rendered as forms; a property with `x-lookup` (`endpoint`, `valueField`, `displayField`)
   becomes a select loaded dynamically from the endpoint – the engine only checks the hint's shape.
5. **Editor**: the layout API is frontend independent; both frontends ship an editor (`/admin/layouts`).

For a third frontend (e.g. React) repeat 1–4: reuse `@middt/ui-core` and a BFF host built with `AddMiddtSpaBff()`.

## Workflow execution and recovery

Workflow JSON uses the framework's general task types (`set`, `setKey`, `log`, `http`, `publish`, `startWorkflow`,
`loadWorkflow`, `fanOut`, `cache`). The `operation` task delegates named domain actions to an
explicitly registered `IWorkflowOperationDispatcher`; identity allowlists its password, MFA and OIDC operations there.
The demo's flows store and query all domain data as JSON inside PostgreSQL-partitioned workflow tables (`WorkflowInstances` +
`WorkflowInstanceData`) and expose dynamic CRUD query endpoints via `WorkflowInstanceDataQuery`. Task definitions remain independent of
application-specific `IWorkflowTask` implementations.

The engine claims an instance with a conditional `Active`/`Faulted` → `Busy` update and, for transition commands, stores
its pending transition and persistable input in that claim. The claim is separate from task execution. On success, the
instance snapshot, history, incidents and follow-up jobs commit together. A claim left Busy beyond `BusyTimeout` is marked Faulted by
`WorkflowJobProcessor`; the retry endpoint can rerun the saved transition when its required input is available.

Scheduled and asynchronous transitions, child cancellation, parent resumption and event delivery use the workflow
database's `WorkflowJobs` table. Workers claim due jobs with leases. Child cancellation waits while the child is Busy;
event handlers may receive duplicates and must be idempotent. A failed job is retried with backoff and, after 20
attempts, removed with an incident. Task data is copied for each in-process attempt; external side effects
need an idempotency key. The built-in HTTP task sends `WorkflowTaskContext.IdempotencyKey` as `Idempotency-Key` unless
the definition supplies that header. An explicit retry is a new command and can use a different key.

Definition versions are retained for running instances. Instance-level access checks and list filters apply each
instance's stored version of `roles` and `instanceAccess`, rather than the newest definition's rules.

## Project map

Folders follow the layers: `sdk/shared` (contracts), `sdk/server` (backend + plugins), `sdk/client/{dotnet,typescript,bff,blazor,angular}`
(everything a frontend needs, one folder per technology), and the same shape for the sample in `demo/{shared,server,client}`;
`spec`, `tests`, `docs` at the root.

| Project | Layer | Notes |
|---|---|---|
| Middt.Framework.Model | contracts | DTOs, settings, query attributes, SignalR contracts |
| Middt.Framework.Expressions | contracts | portable language + DynamicExpresso evaluator (workflows) |
| Middt.Framework.Ui.Abstractions | contracts | layout model, specs, validator, formats, schema generator |
| Middt.Framework.Common | backend / host helpers | security headers, forwarded headers, SignalR hub base, e-mail, export |
| Middt.Framework.Data | backend | repositories, query builder |
| Middt.Framework.Api | backend | hosting, controllers, JWT, ProblemDetails, OpenAPI |
| Middt.Framework.Workflow | backend | engine, endpoints, `IWorkflowViewRenderer` |
| Middt.Framework.Ui.Server | backend | layout store, endpoints, `UiHtmlRenderer` |
| Middt.Framework.Client | client SDK | typed API clients, token refresh, SignalR client |
| Middt.Framework.Bff | frontends | cookie session, token store + refresh, login pages, `/bff/user`, `/api` + `/hubs` proxy (YARP) |
| Middt.Framework.Blazor.Web | Blazor | BFF auth, base pages, workflow screens, layout widgets/hosts/editor (`Ui/`) |
| Middt.Framework.Workflow.Razor | Blazor | Razor workflow view base classes and catalog – separate so the API and shared view libraries can use them without the Blazor frontend |
| sdk/client/typescript/ui-core | TypeScript | `@middt/ui-core` – contract, expressions, formats, layout/data/workflow clients |
| sdk/client/angular | Angular | `@middt/angular` library workspace (demo app: `demo/client/angular/app`) |
