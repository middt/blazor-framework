# Playbook: add a CRUD / directory screen end to end

Middt Framework uses a **100% backend-driven JSON** pattern for server-paged `grid` screens (`/ui/{key}` in both Blazor and Angular) without writing any C# or TypeScript UI code:

---

## Workflow JSON-Backed Screen (Zero C# Code)

In `demo/Middt.Demo.Api`, domain records live as JSON documents inside PostgreSQL-partitioned workflow instances (`WorkflowInstances` + `WorkflowInstanceData`). `WorkflowInstanceDataQuery` dynamically exposes `/api/v1/{workflowKey}/getitems`, `/api/v1/{workflowKey}/getall`, `/api/v1/{workflowKey}/getbyid`, `/api/v1/{workflowKey}/insert`, `/api/v1/{workflowKey}/update`, and `/api/v1/{workflowKey}/delete` for every registered workflow definition—no C# entity, repository, or controller is needed.

### 1. Workflow definition (`Workflows/<key>.json`)
1. Define or reuse a workflow in `demo/Middt.Demo.Api/Workflows/` (e.g., `customer-onboarding.json`, `credit-allocation.json`, `order-payment-orchestration.json`).
2. Declare searchable/sortable fields in `dataSchema.properties` with explicit JSON types (`string`, `integer`, `number`, `boolean`). `WorkflowPartitionManager` automatically creates PostgreSQL partitions and typed B-Tree expression indexes on `WorkflowInstanceData.Data` at startup.

### 2. Screen = JSON layout (`Layouts/<screen-key>.json`)
1. Copy `demo/Middt.Demo.Api/Layouts/customer-directory.json` (or `credit-pipeline.json` / `order-monitor.json`).
2. Set `key`, `version`, `title`, `kind: "page"`, and configure the `grid` widget:
   - `entity`: the workflow key in PascalCase or kebab-case (e.g., `"CustomerOnboarding"` or `"customer-onboarding"` — `WorkflowInstanceDataQuery` resolves both dynamically).
   - `columns`: `{ "field": "key", "title": "Müşteri Anahtarı", "sortable": true, ... }`.
   - `search`: search/filter field definitions (`text`, `number`, `select`, `date`, `checkbox`).
3. Restart the API (or create/edit the layout at runtime in `/ui-admin` / `/admin/layouts`).
4. The screen appears automatically in **both** menus (Blazor `:8080`, Angular `:8081`) at `/ui/<screen-key>`.

---

## Verify
Run `/verify` (or `dotnet test --project tests/Middt.Framework.Tests -- --filter-class "*UiSampleLayoutTests"`), then open both frontends (`/ui/<key>`) to test paging, sorting, and filtering.
