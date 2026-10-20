# Middt Framework

Building blocks for line-of-business applications made of an **ASP.NET Core REST API** and a replaceable frontend
(**Blazor Server** and **Angular** today, both over the same backend): a JSON-defined workflow engine with dynamic
PostgreSQL-partitioned data queries (`WorkflowInstanceDataQuery`), 100% server-driven JSON UI (layouts, grids, forms,
state views, and reusable nested `"widget": "layout"` blocks), JWT/OIDC authentication, BFF cookie sessions, and typed
API clients. See [docs/architecture.md](docs/architecture.md) for the layering.

Targets **.NET 10 (LTS)**.

## Packages

| Layer | Project | Purpose |
|---|---|---|
| Contracts | `Middt.Framework.Model` | Wire contracts: `BaseResponseModel`/`BaseResponseDataModel<T>` (result envelope), auth, workflow and SignalR DTOs, settings. No dependencies. |
| Contracts | `Middt.Framework.Expressions` | The **portable expression language** (identical in .NET and TypeScript, verified by `spec/expressions.conformance.json`) and the richer DynamicExpresso evaluator used by workflow rules. |
| Contracts | `Middt.Framework.Ui.Abstractions` | Server driven UI contract: layout model, widget specs, validator, portable formats, JSON Schema generator. No UI technology. |
| Backend | `Middt.Framework.Common` | Server side helpers for API and frontend hosts: security headers, forwarded headers, local URL checks, SignalR hub base. |
| Backend | `Middt.Framework.Api` | `AddMiddtApi()` / `UseMiddtApi()`, token & API controllers, ProblemDetails, versioning, OpenAPI + Scalar, JWT/OIDC, rate limiting. |
| Backend | `Middt.Framework.Workflow` | Embedded, JSON defined workflow (state machine) engine: manual/automatic/scheduled transitions and message-driven event transitions, tasks (also in the background, outside the instance lock), retries, timers, subflows, history (full / delta / none), multi-node job processing (`SKIP LOCKED`, `LISTEN`/`NOTIFY`), PostgreSQL declarative partitioning & dynamic `WorkflowInstanceDataQuery` – on EF Core, no Dapr. |
| Backend | `Middt.Framework.Ui.Server` | Layout store on EF Core (`ui` schema, revisions, optimistic concurrency), seeding, `MapMiddtUi()` REST API (incl. widgets and JSON Schema), plain C# static HTML renderer. |
| Client SDK | `Middt.Framework.Client` | Typed API clients (`WorkflowApiClient`, `UiApiClient`, `UiDataClient`) on `IHttpClientFactory` + resilience, token refresh, SignalR client. |
| Frontends | `Middt.Framework.Bff` | Backend-for-frontend blocks for any browser frontend: HttpOnly cookie session, server side token store with refresh, Login/Logout pages, `GET /bff/user`, YARP proxy of `/api` + `/hubs` with the session's token and CSRF protection (`AddMiddtSpaBff()` / `UseMiddtSpaBff()`). |
| Blazor | `Middt.Framework.Blazor` | The Blazor frontend: `AddMiddtBlazor()` / `UseMiddtBlazor()`, BFF cookie auth, workflow screens, SignalR; layout renderer under `Ui/` (`AddMiddtUi()`, display + interactive widgets, `UiPageHost`, `UiLayoutHost`, runtime `UiLayoutEditor`). |
| TypeScript | `sdk/client/ui-core` (`@middt/ui-core`) | Types, portable expressions, formats, layout/data/workflow API clients for non-.NET frontends. |
| Angular | `sdk/client/angular` (`@middt/angular`) | Angular 22 frontend library: BFF auth, layout renderer/widgets/editor, workflow list/detail/wizard, SignalR – see [sdk/client/angular/README.md](sdk/client/angular/README.md). Demo app: `demo/angular-app`. |
| Plugins | `Middt.Framework.Rebus` | Messaging on [Rebus](https://github.com/rebus-org/Rebus/wiki) (MIT): `AddMiddtRebus()` with RabbitMQ or in-memory transport, publish/subscribe, send routing, retries and error queue. |

**Middt Identity** (`identity/Middt.Identity`, not a package): the OAuth 2.0 / OpenID Connect server of Middt
applications – one service, configured by appsettings / environment (database, issuer, certificate, clients, users).
Sign-in, registration, password reset and authenticator enrollment run as workflow journeys; account pages and the
`/admin` console are Blazor. See [docs/identity.md](docs/identity.md).

### Repository layout

The repository has two main folders: **`sdk/`** (the framework packages, split into `shared`, `server` and `client`) and
**`demo/`** (the 100% backend-driven JSON demo).

```
sdk/                              the framework (NuGet / npm packages)
  shared/                         Model · Expressions · Ui.Abstractions         contracts, no UI technology
  server/                         Common · Api · Workflow · Ui.Server · Rebus   backend, no frontend dependencies
  client/
    Middt.Framework.Client        .NET client SDK (typed API clients)
    Middt.Framework.Bff           backend-for-frontend host blocks (Blazor + Angular)
    Middt.Framework.Blazor        Blazor Server frontend (Radzen)
    ui-core/                      @middt/ui-core – TypeScript contract, expressions, API clients
    angular/                      @middt/angular – Angular frontend (library workspace)
identity/Middt.Identity           the OAuth 2.0 / OpenID Connect server: one ready to run service (not an SDK package)
demo/                             the 100% backend-driven JSON demo
  Middt.Demo.AppHost              Aspire orchestration of everything
  Middt.Demo.ServiceDefaults      Aspire OpenTelemetry & health defaults
  Middt.Demo.Api                  API (+ Layouts/, Workflows/ seeds)
  Middt.Demo.Blazor               Blazor Server demo
  Middt.Demo.Angular              Angular BFF host
  angular-app/                    Angular demo SPA using @middt/angular as a package
  identity.json                   demo settings of Middt Identity
spec/                             expressions.conformance.json · ui-layout.schema.json (shared by .NET and TypeScript)
tests/  docs/
```

## Architecture

```
 Contracts      Model · Expressions (portable) · Ui.Abstractions · spec/*.json      ← shared by every layer and language
 Backend        Api · Workflow · Ui.Server · Rebus · Common                         ← no Razor/Blazor/Radzen
 Client SDK     Client (typed HttpClient + resilience + token refresh)
 Frontends      BFF: Bff   Blazor: Blazor   Angular: @middt/ui-core · @middt/angular

 Blazor / Angular   UiPageHost / UiLayoutHost / WorkflowInstanceDetail ──► Middt.Framework.Client / @middt/ui-core
                                                                        │  HTTPS / JSON, RFC 9457 ProblemDetails on errors
 ASP.NET Core API                                                       ▼
   MapMiddtWorkflows + MapMiddtUi + WorkflowInstanceDataQuery ──► WorkflowDbContext / UiDbContext (PostgreSQL)
```

The backend never references a frontend package; a frontend talks to it only through REST/JSON and the shared
contracts. The Angular frontend was added exactly that way – a renderer on top of `@middt/ui-core` behind the same BFF – see
[docs/architecture.md](docs/architecture.md).

Design choices:

- **Dependency injection everywhere** – services receive `DbContext` and options via constructor injection; no service locator.
- **Options pattern** – every settings class is bound with `IOptions<T>` (validated on start where it matters, e.g. `JwtSettings`).
- **Result envelope + ProblemDetails** – successful calls return `BaseResponseDataModel<T>`; failures are RFC 9457 ProblemDetails produced by a global `IExceptionHandler`. Exception details are only exposed in Development. The typed clients map both back to `Result = Error` so UI code keeps a single code path.
- **Async all the way** with `CancellationToken`, no `.Result` / `async void`.
- **Observability (OpenTelemetry OTLP + OpenObserve)** – `builder.AddMiddtOpenTelemetry()` in `Middt.Framework.Common` configures OpenTelemetry logs, metrics and traces (including `Middt.Framework.Workflow` transition/task `Activity` spans and `Meter` instruments) and exports them via the standard OpenTelemetry OTLP exporter (`OTEL_EXPORTER_OTLP_ENDPOINT`, `OTEL_EXPORTER_OTLP_PROTOCOL`, `OTEL_EXPORTER_OTLP_HEADERS` or `Telemetry:Otlp` / `Telemetry:Exporters` in `appsettings.json`) to any OTLP-compatible backend (**OpenObserve**, OpenTelemetry Collector, Jaeger, Elastic APM, Grafana Tempo, Seq, Aspire Dashboard) purely from configuration. Health probes are mapped at `/health` and `/alive`.

## Getting started

### API

```csharp
var builder = WebApplication.CreateBuilder(args);

builder.AddMiddtApi(o => o.Title = "My API");
builder.Services.AddMiddtWorkflow(o =>
{
    o.UseDbContext(db => db.UseNpgsql(connectionString));
    o.AddDefinitionsFromDirectory("Workflows");
});
builder.Services.AddMiddtUi(o =>
{
    o.UseDbContext(db => db.UseNpgsql(connectionString));
    o.AddLayoutsFromDirectory("Layouts");
});

var app = builder.Build();
app.UseMiddtApi();
app.MapMiddtWorkflows();
app.MapMiddtUi();
app.Run();
```

OpenAPI documents are served per version at `/openapi/v1.json`, the UI at `/scalar` (in Development by default).

### Blazor Server

```csharp
builder.AddMiddtBlazor();                               // Radzen, BFF session, auth state, ApiSettings
builder.Services.AddMiddtUi();                          // Server-driven JSON layout renderer + widgets
builder.Services.AddMiddtApiClient<WorkflowApiClient>();

var app = builder.Build();
app.UseMiddtBlazor();
```

## Workflows

`Middt.Framework.Workflow` runs long-lived business processes defined in JSON (inspired by vNext, deliberately smaller and
without Dapr): one process, your database, a background job processor.

```csharp
builder.Services.AddMiddtWorkflow(o =>
{
    o.UseDbContext(db => db.UseNpgsql(connectionString)); // PostgreSQL (jsonb) or SQLite
    o.AutoCreateSchema = true;                            // creates the "wf" tables (use migrations in production)
    o.AddDefinitionsFromDirectory("Workflows");           // *.json, validated at startup
});
app.MapMiddtWorkflows("api/workflows");                     // REST API (authorized)
```

A definition ([full example](demo/Middt.Demo.Api/Workflows/customer-onboarding.json)):

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
| Transitions | `Manual` (API, `roles`, optional availability `rule`), `Automatic` (`rule`, one `default` else-branch, chained), `Scheduled` (`after`), `Event` (`event` name; triggered by a message, see [Messages](#messages-event-transitions)), shared transitions (`availableIn`), `$self` (data update), workflow `timeout` |
| Wizards | `step` numbers on states (progress indicator), `"view": { "inline": true }` on transitions (step form rendered inside the screen, pre-filled from `input.into` when a step is revisited), manual transitions with a `rule` (availability condition, e.g. one "Back" per branch); see [credit-allocation.json](demo/Middt.Demo.Api/Workflows/credit-allocation.json) |
| Input | JSON Schema subset (`type`, `properties`, `required`, `additionalProperties`, `enum`, `const`, min/max, lengths, `pattern`, `format`, `items`); the body is only written when the transition declares an `input.schema` (`into` path), or – with `"format": "jsonPatch"` – the body is a JSON Patch on the data at `into` (see [JSON Patch](#updating-data-with-a-json-patch-inputformat-jsonpatch)); `set` computes values (an input without schema ignores the body). UI hint `"x-lookup": { "entity": "CustomerOnboarding", "valueField": "key", "textField": "fullName" }` on a property: both frontends render a select whose options come from `api/v{version}/{entity}/getall` – capped at `MaxGetAllItems` (1000), so return the relevant rows first, e.g. newest first (shape checked at load time, the value itself is validated by the property's other keywords) |
| Tasks | `onEntry`, `onExit`, `onExecute`; general `set`, `setKey` (gives the instance its business key once it is known, e.g. `CUST-{{ data.identityNumber }}`), `log`, `http` (templated, host placeholders forbidden, values URL-encoded), `publish` (`IWorkflowMessagePublisher`, e.g. Rebus), `startWorkflow`, `loadWorkflow`, `fanOut`, `cache`, and `operation` (dispatches an explicitly allowed application operation); `when` condition; `"background": true` on a state's `onEntry` task runs it outside the instance lock (see [Long tasks](#long-tasks-background)) |
| Errors | per task / workflow `onError`: `maxAttempts` + exponential `delay`, then `Fault` (incident, `retry` endpoint), `Continue` or `Transition` (rolls the failed step back, then runs the given transition) |
| Subflows | `SubFlow` state starts a child (`input` mapping, `outputInto`); the parent continues when the child finishes (`child.subType` in rules); cancelling the parent cancels the child |
| Expressions | C#-like (DynamicExpresso) over `data`, `input`, `child`, `result`, `user` (`user.IsInRole("x")`), `instance`, `now`; reflection and type access are disabled; `{{ expr }}` templates in task config |
| History | `"history": "full"` (default: data snapshot per transition), `"delta"` (mostly only the change, as a JSON Patch in `Patch`, with a full snapshot for the first row, the row that finishes the instance, every `historySnapshotEvery` rows (default 20) and whenever the patch would not be smaller than the data – so any version is rebuilt from the nearest snapshot with at most that many patches; `WorkflowEngine.HistoryDataAsync`) or `"none"` (no history rows; the instance data itself is still saved on every transition) – see [History modes](#history-modes-and-how-json-patch-is-used) |
| Runtime | Busy flag via conditional UPDATE (concurrent requests → 409), a heartbeat that keeps a running claim alive (`BusyHeartbeatInterval`), atomic final state + history + follow-up jobs, incidents, idempotent start by business key, async transitions (202), durable timers and follow-up jobs with renewed leases, claimed with `FOR UPDATE SKIP LOCKED` on PostgreSQL and woken across nodes by `LISTEN`/`NOTIFY` (multi-instance safe, no polling delay), crash recovery of Busy instances, queued `IWorkflowEventHandler` notifications after commit |

The engine first records a Busy claim with the pending transition and persistable input. It then runs tasks and commits the resulting instance data, history, incidents and follow-up jobs together. While the tasks run, the claim refreshes `BusySince` every `BusyHeartbeatInterval` (default 1 minute), so a slow task is never mistaken for a crash. If the process stops, the heartbeat stops too and the job processor marks the instance Faulted once `BusySince` is older than `BusyTimeout`; `POST …/retry` reruns its pending transition. Input fields declared `sensitive` are excluded from persisted input. If a recovered transition requires those fields, the retry endpoint cannot reconstruct them and the instance needs operator remediation. Instances retain their definition version; list permissions use each instance's version, including its `roles` and `instanceAccess` settings.

Task attempts use separate data snapshots. A failed attempt cannot leave partial data in the next attempt. Custom tasks receive `WorkflowTaskContext.IdempotencyKey`, which stays the same during the attempts of one command; the built-in HTTP task sends it as `Idempotency-Key` unless the definition supplies that header. External systems must deduplicate with this key if retries could repeat a side effect. Configure a stable business key for `startWorkflow` when the same child must not be started twice. A later explicit retry of a faulted instance is a new command and may use a different key.

Child cancellation, parent resumption and event delivery are written as jobs in the same commit as the state change. The engine tries them after commit for prompt results; the background processor handles work left by failures or restarts. Event delivery can repeat, so handlers should be idempotent. A job that still fails after 20 attempts is removed and recorded as a workflow incident for investigation; delivery is not guaranteed after that point.

REST API (all responses use the `BaseResponseDataModel<T>` envelope; errors are ProblemDetails):
`GET /api/workflows`, `GET /{workflow}`, `POST /{workflow}/instances`, `GET /{workflow}/instances`,
`GET /{workflow}/instances/{id}` (includes the caller's available transitions and their input schemas),
`POST /{workflow}/instances/{id}/transitions/{key}[?async=true]`, `GET …/history`, `GET …/incidents`, `POST …/retry` (a transition body is an object, or a JSON Patch array for `input.format: jsonPatch`),
`POST /{workflow}/instances/{id}/messages/{message}` and `POST /{workflow}/messages/{message}?key=…` (`[&bufferFor=PT10M]`; 200 delivered, 202 buffered).

### Messages (event transitions)

A transition with `"trigger": "Event"` runs when a message of its `event` name reaches the instance. The message body is
the transition input (validated by `input.schema`, written with `into`); several event transitions may listen for the same
message and the first whose `rule` holds is taken – `input` is the message body there:

```jsonc
{ "key": "paid", "target": "paid", "trigger": "Event", "event": "payment.received", "rule": "input.amount >= data.total",
  "input": { "schema": { "type": "object", "properties": { "amount": { "type": "number" } } }, "into": "payment" } },
{ "key": "partial", "target": "$self", "trigger": "Event", "event": "payment.received",
  "input": { "schema": { "type": "object", "properties": { "amount": { "type": "number" } } }, "into": "partialPayment" } }
```

A message is correlated to one instance by its id or by the workflow's business key
(`WorkflowEngine.DeliverMessageAsync(new WorkflowMessage { Name, Workflow, Key | InstanceId, Data, BufferFor }, user)` or the
REST endpoints above). The sender needs read access to the instance and must satisfy the `roles` of at least one event
transition for that message ([invoice-collection.json](demo/Middt.Demo.Api/Workflows/invoice-collection.json) is a
complete example: full and partial payments, a late payment after the due date). When the instance cannot take the message now (busy, faulted, or in a state without a matching
transition) it is rejected with 409 – or, with `BufferFor`, kept as a durable job and delivered as soon as the instance enters
a state that waits for it; it is dropped when it expires. Event transitions are never offered as buttons.

### Long tasks (background)

A transition that calls a slow system holds the instance Busy for as long as the call takes. Mark such a state `onEntry`
task `"background": true` instead: the state is committed first and the instance stays Active (readable, cancellable,
other transitions allowed) while the task runs as a job. When it finishes, its data changes are merged into the current
data (changes made meanwhile are kept), a `$background` history row is written, the remaining `onEntry` tasks and the
automatic transitions run. The outcome is stored on the job before it is applied, so a busy instance or a restart never
runs the task twice; a result that arrives after the instance left the state is discarded. A failure follows the task's
`onError` (`Fault` → `retry` runs the task again, `Transition`, `Continue`). Background tasks are allowed in `onEntry` of
non-finish, non-subflow states only, and not for `setKey`; they receive no transition `input`. Example: the `post-ledger`
task of [invoice-collection.json](demo/Middt.Demo.Api/Workflows/invoice-collection.json).

### History modes (and how JSON Patch is used)

Every transition writes one row to `WorkflowHistory` (who, when, from → to, input, executed tasks). The `history` setting
of a definition decides what that row keeps of the instance **data**:

```jsonc
{ "key": "invoice-collection", "history": "delta", "historySnapshotEvery": 10, ... }
```

| Mode | `Data` column | `Patch` column | Use it for |
|---|---|---|---|
| `full` (default) | the whole data after the transition | – | small or short-lived flows; the old behaviour |
| `delta` | only on checkpoint rows | the change made by this transition | long-lived records with many updates and large data |
| `none` | no row at all | – | one-shot or high-volume flows whose audit lives elsewhere |

The instance's current data (`WorkflowInstanceData`) is saved on every transition in all three modes; the mode only changes
the history.

**Nothing changes for clients.** A client never sends or receives a patch: it calls transitions and sends messages exactly
as before (`POST …/messages/payment.received?key=INV-FTR-2026-0001` with `{ "amount": 5000, "reference": "EFT-5000" }`).
After the transition the engine compares the data before and after it and stores the difference as an
[RFC 6902](https://www.rfc-editor.org/rfc/rfc6902) JSON Patch – a list of `add` / `replace` / `remove` operations on JSON
paths. These are the rows the `invoice-collection` demo writes for a start and two partial payments:

| Transition | `DataVersion` | `Data` | `Patch` |
|---|---|---|---|
| `start` | 1 | `{"invoiceNo":"FTR-2026-0001","customerName":"Anadolu Lojistik A.Ş.","total":12500,…}` | `null` |
| `partial-payment` | 2 | `null` | `[{"op":"add","path":"/lastPartialPayment","value":{"amount":5000,"reference":"EFT-5000"}}]` |
| `partial-payment` | 3 | `null` | `[{"op":"replace","path":"/lastPartialPayment/amount","value":7000},{"op":"replace","path":"/lastPartialPayment/reference","value":"EFT-7000"}]` |

Objects are compared member by member, so the third row only names the two values that changed; arrays and plain values
are replaced whole. `remove` and an explicit `"value": null` are different operations, so a field set to `null` is never
confused with a deleted one.

**Checkpoints.** In `delta` mode a row is a full snapshot (in `Data`, `Patch` empty) instead of a patch when it is the first
row, when it finishes the instance, when `historySnapshotEvery` patch rows (default 20) followed the last snapshot, or when
the patch would not be smaller than the data itself. Any version is therefore rebuilt from the nearest snapshot before it
with at most `historySnapshotEvery` patches, and the final data of a finished instance is a single read.
`WorkflowInstance.HistoryPatchCount` counts the patches since the last snapshot, so deciding never reads the history.

**Reading it back.** `WorkflowEngine.HistoryDataAsync` returns the data after every row, whatever the mode wrote
(snapshot rows reset, patch rows are applied in order). `GET …/history` is unchanged: it lists transitions, triggers,
actors and tasks, not data. For ad-hoc analysis in SQL, `Patch` is `jsonb`:

```sql
SELECT "DataVersion", "TransitionKey", coalesce("Data", "Patch") AS change
FROM wf."WorkflowHistory" WHERE "WorkflowKey" = 'invoice-collection' AND "InstanceId" = @id ORDER BY "Id";
```

These history patches are produced by the engine; a client can also *send* a patch to change data – see
[Updating data with a JSON Patch](#updating-data-with-a-json-patch-inputformat-jsonpatch).

**The same patch merges background results.** A `"background": true` task works on a copy of the data while the instance
stays usable. When it finishes, only its patch is applied to the data as it is then, so an update made meanwhile (for
example by a `$self` transition) is kept: the ledger call of `invoice-collection` contributes just
`[{"op":"add","path":"/ledgerResponse",…}]`. If both touch the same array, the task's version of that array wins.

### Updating data with a JSON Patch (`input.format: jsonPatch`)

A transition normally takes an object that is merged into the data. With `"format": "jsonPatch"` it takes an
[RFC 6902](https://www.rfc-editor.org/rfc/rfc6902) JSON Patch instead: the client says exactly what changes – one field,
a removal, an item appended to a list – without resending the rest, and can guard the change with a `test` operation.

```jsonc
{ "key": "update-billing-contact", "target": "$self",
  "input": { "format": "jsonPatch", "into": "billingContact",
             "schema": { "type": "object", "additionalProperties": false,
                         "properties": { "email": { "type": "string", "format": "email" }, "phone": { "type": "string" },
                                         "address": { "type": "string" } } },
             "set": { "contactUpdatedBy": "user.Name" } } }
```

```http
POST /api/workflows/invoice-collection/instances/{id}/transitions/update-billing-contact
[ { "op": "test",    "path": "/phone", "value": "02124440123" },
  { "op": "replace", "path": "/phone", "value": "02124440999" },
  { "op": "remove",  "path": "/address" } ]
```

- **Paths are relative to `into`**, which is required: the patch can only change the part of the data the transition
  owns (`data.billingContact` above), never fields written by tasks or other transitions. `schema` is required too and
  validates that part after the patch.
- **Operations**: `add`, `remove`, `replace`, `move`, `copy`, `test`; array indexes and `-` (append) work as in the RFC.
  The patch is atomic – applied completely or not at all. An invalid operation or a result the schema rejects answers
  400, a failed `test` 409 (an optimistic check: "only change the phone if it is still the one I saw"); either way the
  instance stays as it was and Active.
- **Body**: a bare JSON array over HTTP; programmatic callers pass `{ "patch": [ … ] }` (that is also how it travels inside
  the engine). `set` expressions, tasks and the rest of the transition run as usual.
- **History**: the client's patch is the transition input, so it is stored in the history row's `Input`; with
  `history: delta` the row's `Patch` holds the resulting change from the data root (`/billingContact/phone`).
- **Frontends**: `AvailableTransitionModel.InputFormat` is `jsonPatch`. Blazor and Angular open the form filled with the
  current values at `into` and send the difference as a patch (`WorkflowUi.TransitionInput` / ui-core
  `workflowTransitionInput`); a cleared field becomes `remove`.
- **Not allowed** on the start transition, on event transitions (messages carry objects), with `sensitive` properties or
  asynchronously (`?async=true`) – a rejected patch must reach the caller.
- The implementation is `JsonPatch` in `Middt.Framework.Expressions` (`ApplyStrict`, `Diff`); the TypeScript `jsonPatchDiff`
  is its twin, and both pass [`spec/json-patch.conformance.json`](spec/json-patch.conformance.json).


Blazor: register `builder.Services.AddMiddtApiClient<WorkflowApiClient>();` and use the components from
`Middt.Framework.Blazor.Base.Component.Workflow` – `WorkflowInstanceList` (paged list, columns generated from `dataSchema`, start dialog generated from the
start input schema), `WorkflowInstanceDetail` (state, data, history, incidents, retry, one button per available transition
with a form generated from its input schema) and `JsonSchemaForm`. A schema property with
`"x-lookup": { "entity": "CustomerOnboarding", "valueField": "key", "textField": "fullName" }` is rendered as a select of the
entity's rows (same shape as the layout field `lookup`; the value type follows the property `type`). The demo has one
generic page per workflow, `/workflow/{key}` (`demo/Middt.Demo.Blazor/Pages/Workflow/WorkflowPage.razor`,
menu group "İş Akışları"); `?start=1` opens the start dialog once (`WorkflowInstanceList.OpenStart`), `key` prefills
its business key and every other parameter named like a start input property prefills that field (typed by the schema,
e.g. `/workflow/customer-onboarding?start=1`); nothing starts before "Başlat", and these
parameters are removed from the URL once the dialog is open. A start with the business key of an existing instance
returns that instance (200) with `messageList` = `WorkflowEndpoints.ExistingInstanceMessage`; both frontends then show
"Mevcut akış açıldı" instead of "Akış başlatıldı".

Compared to vNext this engine intentionally leaves out the separate execution host, Dapr (jobs, locks, pub/sub, state),
an external inbox/outbox relay, Roslyn C# scripts, schema-per-flow storage, views/functions/extensions and long polling.
Follow-up work uses the workflow database's job table. Tasks run in-process, so their external side effects must be
idempotent across retries.

## Server driven UI (JSON layouts)

Screens are data: a **layout** is a JSON tree of registered **widgets**, stored in the database and editable at
runtime – no deployment for a new list page, a changed column or a new workflow state screen. Layouts never contain
code or markup: only widget names, properties and sandboxed expressions, validated against the widget catalog.
A layout is frontend independent: Blazor renders it today (`Middt.Framework.Blazor`, namespace `Middt.Framework.Blazor.Ui`), the API renders it to static
HTML (`UiHtmlRenderer`), and Angular renders it with `@middt/angular` (on `@middt/ui-core`).

```jsonc
{
  "$schema": "../../../../spec/ui-layout.schema.json",
  "key": "customer-directory",
  "title": "Müşteri Rehberi (Workflow JSON)",
  "kind": "page",
  "version": 2,
  "body": [
    { "widget": "heading", "text": "Müşteri Rehberi (Workflow JSON Verisi)" },
    { "widget": "link", "text": "Yeni Müşteri Kaydı Başlat", "href": "/workflow/customer-onboarding?start=1", "style": "button" },
    {
      "widget": "grid",
      "id": "customers",
      "entity": "CustomerOnboarding",
      "columns": [
        { "field": "key", "title": "Müşteri Anahtarı", "width": "170px" },
        { "field": "fullName", "title": "Ad Soyad / Ünvan" },
        { "field": "segment", "title": "Segment", "width": "120px" },
        { "field": "kycScore", "title": "KYC Skoru", "width": "110px" },
        { "field": "stateTitle", "title": "Akış Durumu", "width": "160px" }
      ],
      "search": [
        { "field": "fullName", "label": "Ad Soyad / Ünvan" },
        { "field": "segment", "label": "Segment" }
      ]
    }
  ]
}
```

| Widget | Purpose |
|---|---|
| `heading`, `text`, `alert`, `badge`, `divider`, `link` | Display widgets; text properties are `{{ }}` templates |
| `fields` | Label/value list with formats (`N0`–`N6`, `F0`–`F6`, `d`, `D`, `g`, `t`) |
| `section`, `columns`, `list` | Containers (`list` repeats its children per array item as `row`) |
| `layout` *(reusable / interactive)* | Embeds another `kind: "view"` layout by `key` with optional `data` scope expression and `title` (max nesting depth 5) |
| `grid` *(interactive)* | Server paged/sorted grid over a workflow (`entity`): search fields, fixed `filter`, row actions, create/edit/delete dialog |
| `record` *(interactive)* | Loads one entity by `recordId` and renders its `children` with `record` in scope |

- Every node accepts `visibleWhen` (e.g. `user.IsInRole("manager")`, `data.kycScore >= 750`). Variables: `data`,
  `query`, `row`, `record`, `instance`, `user`, `now`. Expressions use the **portable language**: member access with
  null propagation, `! - * / % + < <= > >= == != && || ?? ?:`, text methods (`Contains`, `StartsWith`, `ToUpper`, …),
  `Math.Round/Abs/Floor/Ceiling/Min/Max`; `==` is strict (`query.id == "42"`, not `42`). Unknown variables or methods
  are rejected when the layout is saved. Formats: `N0`–`N6`, `F0`–`F6`, `d`, `D`, `g`, `t`. Form/search field types: `text`, `textarea`, `number`,
  `integer`, `date`, `datetime`, `checkbox`, `select` (static `options` or a `lookup` entity) and `picker` (search
  dialog over a lookup entity).
- **Reusable nested layouts (`"widget": "layout"`)**: Common UI blocks (such as `workflow-footer.json`, `customer-summary-card.json`, `risk-eval-card.json`) can be defined once as a `kind: "view"` layout and embedded inside any number of parent layouts via `{ "widget": "layout", "key": "workflow-footer", "data": "<optional-sub-object-expr>" }`. See [`docs/ai/backend-driven-json-ui.md`](docs/ai/backend-driven-json-ui.md).
- **API**: `builder.Services.AddMiddtUi(o => o.UseDbContext(...).AddLayoutsFromDirectory("Layouts"))` and
  `app.MapMiddtUi()`. Seed files are validated at startup (a broken file stops the app) and overwrite stored layouts
  only when their `version` is higher, so runtime edits survive restarts. Reads are open to authenticated users
  (filtered by the layout's `roles`); `PUT/DELETE/validate/revisions` need `MiddtUiOptions.AdminRole` (`ui-admin`).
  `PUT` carries the version it is based on (0 = new) – a stale save gets 409; every change keeps a revision.
- **Blazor & Angular**: `<UiPageHost Key="@Key" />` / `<UiLayoutHost />` (Blazor) and `<middt-layout-page />` / `<middt-layout-host />` (Angular), plus `<UiLayoutEditor />` / `<middt-layout-editor />` at `/ui-admin` (edit JSON, validate on the server, preview, save, revisions, delete).
  Grid/record/layout widgets call the API **with the signed-in user's token**, so API authorization is unchanged;
  a layout can only show what the user may already read.
- **Workflows**: every state in the sample uses `"view": { "layout": "kyc-review" }` (or `credit-review`, `order-review`, `order-result`, `order-escalated`, `risk-detail`);
  both frontends render it automatically with `data` and `instance`.
- **Editors**: `GET api/ui/schema` (also committed as `spec/ui-layout.schema.json`) is the JSON Schema of layouts;
  seed files reference it with `"$schema"` for completion in VS Code / Rider.
- **Custom widgets**: register the spec in the API – `o.AddWidget(spec, html)` (optional static HTML) – and an
  implementation in each frontend: `AddMiddtUi(w => w.Add<MyWidget>(spec))` in Blazor, `provideMiddtUiWidgets({ name, component })` in Angular.

All screens in `demo/Middt.Demo.Api/Layouts` (`executive-overview`, `customer-directory`, `credit-pipeline`, `order-monitor`, `kyc-review`, `credit-review`, `order-review`, `order-result`, `order-escalated`, `risk-detail`, `customer-summary-card`, `risk-eval-card`, and `workflow-footer`) are 100% backend-driven JSON layouts.

## Messaging (Rebus)

```csharp
builder.Services.AddMiddtRebus(builder.Configuration, rebus => rebus
    .Subscribe<WorkflowNotification>()     // pub/sub (RabbitMQ topic exchange)
    .Map<CreateInvoice>("billing"));       // bus.Send(...) routes
builder.Services.AddMiddtRebusHandlersFromAssemblyOf<WorkflowNotificationHandler>();

public sealed class WorkflowNotificationHandler : IHandleMessages<WorkflowNotification>
{
    public Task Handle(WorkflowNotification message) => ...;
}

await bus.Publish(new WorkflowNotification(...));   // inject Rebus.Bus.IBus
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

### Secure defaults

| Area | Default | Opt out / configure |
|---|---|---|
| OpenAPI + Scalar | Development only | `MiddtApiOptions.EnableOpenApi = true` |
| Page size | `WorkflowInstanceDataQuery` clamped (`getitems` ≤ 500, `getall` ≤ 1000) | – |
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

**Hardening.** ProblemDetails without exception details outside Development; parameterized SQL queries and
white-listed sort columns; security headers (CSP, `X-Frame-Options`, `X-Content-Type-Options`, `Referrer-Policy`,
`Permissions-Policy`, COOP) and HSTS; CORS requires explicit origins; client IPs come from `X-Forwarded-For` only for
trusted proxies listed in `ForwardedHeaders:KnownProxies/KnownNetworks`; audit logs for login success/failure and refresh
token reuse; personal data is kept out of logs; vulnerable transitive packages are pinned in `Directory.Packages.props`.

## Running the sample

The demo (`demo/`) runs on PostgreSQL (`middt_workflow` + `middt_identity`), Redis, RabbitMQ (Rebus), the OAuth 2.0 / OpenID Connect identity provider, the API, the Blazor UI and the Angular UI. Both UIs sign in at the identity provider (authorization code + PKCE through their BFF) and the API accepts its access tokens.

**With .NET Aspire (recommended)** – requires Docker or Podman:

```bash
dotnet run --project demo/Middt.Demo.AppHost
```

The Aspire dashboard shows all resources, logs, traces and metrics. Set the PostgreSQL password once with
`dotnet user-secrets set Parameters:postgres-password "<password>" --project demo/Middt.Demo.AppHost`
(Aspire prompts for missing parameters otherwise).

**With Docker Compose:**

```bash
cp .env.example .env    # then set IDP_BLAZOR_CLIENT_SECRET, IDP_ANGULAR_CLIENT_SECRET, POSTGRES_PASSWORD, RABBITMQ_DEFAULT_USER / _PASS
docker compose up --build
```

Blazor UI: <http://localhost:8080> · Angular UI: <http://localhost:8081> (same screens, layouts and workflows) ·
identity provider: <http://localhost:8083> (login, registration, password reset, authenticator, `/admin` console,
`/dev/mailbox` for verification codes) · OpenObserve (OTLP logs, metrics, traces): <http://localhost:5180>
(`admin@middt.local` / `Complexpass#123` unless overridden in `.env`) · demo users `test@test.com` / `test`
(roles `manager`, `ui-admin`, `idp-admin`) and `user@test.com` / `test` · layout editor: `/ui-admin` · API reference:
<http://localhost:8180/scalar>.
Signing in once at the IdP signs you into both UIs (single sign-on); see [docs/identity.md](docs/identity.md).

"Fatura Tahsilatı" (`invoice-collection`) can be driven by hand: a manager records a partial or full payment with the
buttons "Kısmi Ödeme Kaydet" / "Ödemeyi Kaydet (Tam)", the invoice moves to "Muhasebe Mutabakatı" and – after the
background ledger call – to "Tahsil Edildi". "Fatura İletişim Bilgilerini Güncelle" edits the billing contact with a
JSON Patch (the form opens with the current values; only the changes are sent). The payment steps are also triggered by a
payment system's message (`POST /api/workflows/invoice-collection/messages/payment.received?key=INV-…`). Postman
collection [06](postman/06-Invoice-Collection.postman_collection.json) runs all of it, including JSON Patch requests with
`add`, `test` + `replace`, a failing `test` (409) and `remove`. All collections are described in [postman/README.md](postman/README.md).

## Development

**AI-assisted development:** [`AGENTS.md`](AGENTS.md) (map, commands, conventions, definition of done) plus nested
`AGENTS.md` files per area and task playbooks in [`docs/ai/`](docs/ai/README.md). Tool entry points: `CLAUDE.md`
(Claude Code, slash commands `/verify`, `/add-entity`, `/add-widget`, `/add-workflow`), `.github/copilot-instructions.md`,
`.cursor/rules/middt.mdc`.

```bash
dotnet build Middt.Framework.slnx
dotnet test --solution Middt.Framework.slnx
dotnet pack Middt.Framework.slnx -c Release -o artifacts
(cd sdk/client/ui-core && node --test test/)     # TypeScript core, same conformance suite
(cd sdk/client/ui-core && npm ci && npm run build)            # @middt/ui-core package
(cd sdk/client/angular && npm ci && npx ng build middt-angular)           # @middt/angular package
(cd demo/angular-app && npm ci && npx ng build sample)             # Angular demo app (consumes both packages)
(cd tests/e2e && npm ci && npx playwright install chromium && npm test)    # full-stack browser + PostgreSQL smoke tests (Compose running)
```

`spec/ui-layout.schema.json` is generated from the widget catalog; after changing widget specs regenerate it with
`MIDDT_UPDATE_SPEC=1 dotnet test --project tests/Middt.Framework.Tests` (a test fails while it is out of date).

Package versions are managed centrally in `Directory.Packages.props`; shared build settings live in `Directory.Build.props`.

## License

GPL-2.0-only
