# sdk/server – backend (read the root AGENTS.md first)

Common · Api · Workflow · Ui.Server · plugins (Rebus). **Must not reference any client/frontend package**
(Client, Bff, Blazor.Web, Radzen) – LayeringTests enforce it.

## Api (`Middt.Framework.Api`)
- Hosting: `AddMiddtApi()` / `UseMiddtApi()` (ProblemDetails, versioning, JWT, rate limiting, CORS,
  compression, OpenAPI+Scalar **Development-only by default**).
- Controllers: `BaseController`, `BaseTokenController` (refresh token rotation & revocation via `IRefreshTokenStore`).

## Workflow (`Middt.Framework.Workflow`)
- Definitions are JSON (`demo/server/Middt.Sample.Api/Workflows`), validated at startup by `WorkflowRegistry`
  (`WorkflowDefinitionValidator`) – add validation for every new definition property.
- `WorkflowEngine` runs transitions with an optimistic claim (Busy + revision), automatic chains, subflows, jobs
  (`WorkflowJobProcessor`), history and incidents. Keep round trips low (there is a test pinning the command count).
- Persist the pending command in the Busy claim. Final state, history and follow-up jobs commit together; event
  delivery, parent resume and child cancellation are replayable jobs. Do not delete pending event jobs when an instance
  finishes. Handlers and external task side effects must be idempotent; task attempts share an idempotency key but an
  explicit later retry can get a new one. Never persist `input.sensitive` values.
- Access: definition `roles`, transition `roles`, `instanceAccess` (owner mode + supervisor roles). Every instance-level
  entry point must go through `EnsureCanAccess`; list filtering uses each instance's stored definition version.
- Errors shown to callers are hidden unless `ExposeErrorDetails`; the `http` task never follows redirects and honours
  `AllowedHttpHosts` – keep both.
- Static view HTML: `IWorkflowViewRenderer` implementations (e.g. `WorkflowLayoutRenderer` using `UiHtmlRenderer`).
- Persistence & Querying: `WorkflowDbContext` (schema `wf`, PostgreSQL declarative partitioning per workflow key, `jsonb` columns, functional B-Tree indexes from `dataSchema`), `WorkflowInstanceDataQuery` (exposes `/api/v1/{workflow}/getitems|getall|getbyid|insert|update|delete` over workflow JSON instances); ids use UUIDv7 (`Guid.CreateVersion7()`).

## Ui.Server (`Middt.Framework.Ui.Server`)
- `UiLayoutStore` (EF, schema `ui`, revisions, optimistic version, cached snapshot with a reload lock), seeding from
  directories (higher `version` wins), endpoints `MapMiddtUi()` (admin role for writes), `UiHtmlRenderer` (plain C#,
  HTML-encodes everything – never write raw data with `Raw()`).

## Tests
`tests/Middt.Framework.Tests/{Api,Workflow,Ui,Messaging,Security}` – hosts: `TestApiHost`, `WorkflowTestHost`
(SQLite in-memory, FakeTimeProvider, header based fake auth), `UiTestHost`.
