# sdk/server – backend (read the root AGENTS.md first)

Common · Data · Api · Workflow · Ui.Server · plugins (Redis, Rebus). **Must not reference any client/frontend package**
(Client, Bff, Blazor.Web, Workflow.Razor, Radzen) – LayeringTests enforce it.

## Api / Data
- Hosting: `AddMiddtApi()` / `UseMiddtApi()` (ProblemDetails, versioning, JWT, rate limiting, CORS, HybridCache,
  compression, OpenAPI+Scalar **Development-only by default**).
- CRUD: controller `BaseCrudController<TModel, TRepository>` (use `BaseCrudControllerWithAuth` for authenticated APIs) +
  repository `BaseCrudRepository<TModel, TContext>`. Routes `api/v{version}/{entity}/getitems|getall|getbyid|insert|update|delete`.
- Authorization hooks: override `AuthorizeAsync` (record level, IDOR; denied → 404) and `ApplyAccessFilter` (row level).
  Defaults allow every authenticated user – production entities usually need an override.
- Mass assignment: updates go through `ApplyUpdate` (only editable scalar columns); never `context.Update(clientModel)`.
- Filters: `QueryFilterBuilder` builds expression trees from the request model + `[Query*]` attributes; values must stay
  **parameters** (tests assert no literals in SQL). Page size is clamped (`MaxPageSize`, `MaxGetAllItems`).
- Keyless entities (views) need `DefaultSortProperty` for deterministic paging.
- No `.Result`/`.Wait()`, `AsNoTracking()` for reads, `ExecuteUpdate/ExecuteDelete` for bulk.

## Workflow (`Middt.Framework.Workflow`)
- Definitions are JSON (`demo/server/Middt.Sample.Api/Workflows`), validated at startup by `WorkflowRegistry`
  (`WorkflowDefinitionValidator`) – add validation for every new definition property.
- `WorkflowEngine` runs transitions with an optimistic claim (Busy + revision), automatic chains, subflows, jobs
  (`WorkflowJobProcessor`), history and incidents. Keep round trips low (there is a test pinning the command count).
- Access: definition `roles`, transition `roles`, `instanceAccess` (owner mode + supervisor roles). Every instance-level
  entry point must go through `EnsureCanAccess`.
- Errors shown to callers are hidden unless `ExposeErrorDetails`; the `http` task never follows redirects and honours
  `AllowedHttpHosts` – keep both.
- Static view HTML: `IWorkflowViewRenderer` implementations (Razor components via `UseViews`, layouts via the host).
- Persistence: `WorkflowDbContext` (schema `wf`), indexes created at startup with `AutoCreateSchema`; ids on SQL Server
  come from the sequential generator.

## Ui.Server
- `UiLayoutStore` (EF, schema `ui`, revisions, optimistic version, cached snapshot with a reload lock), seeding from
  directories (higher `version` wins), endpoints `MapMiddtUi()` (admin role for writes), `UiHtmlRenderer` (plain C#,
  HTML-encodes everything – never write raw data with `Raw()`).

## Tests
`tests/Middt.Framework.Tests/{Api,Data,Workflow,Ui,Messaging,Security}` – hosts: `TestApiHost`, `WorkflowTestHost`
(SQLite in-memory, FakeTimeProvider, header based fake auth), `UiTestHost`.
