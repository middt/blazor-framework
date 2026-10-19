# Playbook: add a CRUD / directory screen end to end

Middt Framework supports two backend patterns for server-paged `grid` screens (`/ui/{key}` in both Blazor and Angular) without writing any frontend UI code:

---

## Option A (Recommended – Zero C# Code): Workflow JSON-Backed Screen

In `demo/server/Middt.Sample.Api`, domain records live as JSON documents inside PostgreSQL-partitioned workflow instances (`WorkflowInstances` + `WorkflowInstanceData`). `WorkflowInstanceDataQuery` dynamically exposes `/api/{workflowKey}/GetListWithModel`, `/api/{workflowKey}/CountWithModel`, and `/api/{workflowKey}/GetById` for every registered workflow definition—no C# entity, repository, or controller is needed.

### 1. Workflow definition (`Workflows/<key>.json`)
1. Define or reuse a workflow in `demo/server/Middt.Sample.Api/Workflows/` (e.g., `customer-onboarding.json`, `credit-allocation.json`, `order-payment-orchestration.json`).
2. Declare searchable/sortable fields in `dataSchema.properties` with explicit JSON types (`string`, `integer`, `number`, `boolean`). `WorkflowPartitionInitializer` automatically creates PostgreSQL partitions and typed B-Tree expression indexes on `WorkflowInstanceData.Data` at startup.

### 2. Screen = JSON layout (`Layouts/<screen-key>.json`)
1. Copy `demo/server/Middt.Sample.Api/Layouts/customer-directory.json` (or `credit-pipeline.json` / `order-monitor.json`).
2. Set `key`, `version`, `title`, `kind: "crud"`, `icon` (Material icon shown in the sidebar menu), and configure the `grid` widget:
   - `endpoint`: the workflow key (e.g., `"customer-onboarding"` or `"CustomerOnboarding"` — `WorkflowInstanceDataQuery` resolves both dynamically).
   - `columns`: `{ "field": "CustomerNo", "header": "Müşteri No", "sortable": true, ... }`.
   - `fields`: search/filter and form field definitions (`text`, `number`, `select`, `date`, `boolean`).
3. Restart the API (or create/edit the layout at runtime in `/admin/layouts`).
4. The screen appears automatically in **both** menus (Blazor `:8080`, Angular `:8081`) at `/ui/<screen-key>`.

---

## Option B: EF Core Relational Entity (`BaseCrudRepository` + `BasePolicyCrudController`)

When building a line-of-business service backed by a dedicated relational EF Core table:

1. **Entity & Search Model**: Create the EF Core entity inheriting `BaseModel` and add `[NotMapped]` / `[Query*]` filter properties (search-only properties use `[QueryIsNoFilter]` or a `[Query*]` attribute from `Middt.Framework.Model`). Register the `DbSet<T>` in your `DbContext`.
2. **Repository**: Create `MyEntityRepository : BaseCrudRepository<MyEntity, MyDbContext>` and register it in DI. Override `DefaultSortProperty` for keyless views.
3. **Controller**: Derive from `BasePolicyCrudController<MyEntity, MyEntityRepository>` (which enforces row-level security via `ApplyAccessFilter` and `AuthorizeAsync`).
4. **Screen = JSON layout**: Create `Layouts/<key>.json` (`kind: "crud"`) with `"widget": "grid"` and `"endpoint": "MyEntity"`, matching the controller route.

---

## Verify
Run `/verify` (or `dotnet test --project tests/Middt.Framework.Tests -- --filter-class "*UiSampleLayoutTests"`), then open both frontends (`/ui/<key>`) to test paging, sorting, and filtering.
