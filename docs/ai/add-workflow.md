# Playbook: add or change a workflow

Definitions are JSON(C) files in `demo/Middt.Demo.Api/Workflows/` (`customer-onboarding.json`, `credit-allocation.json`, and `order-payment-orchestration.json` are full examples: `dataSchema`, `icon`, rules, subflows, `fanOut`, `loadWorkflow`, `cache`, roles, timers, and backend-driven `view.layout` screens).

1. **Definition & Dynamic Menu / List Columns**:
   - Top-level properties: `key`, `version` (bump `version` when updating a seeded file so `WorkflowSeedService` updates the DB), `title`, `icon` (Material icon rendered automatically in the sidebar menu of both frontends), `roles`, optional `instanceAccess` (`owner` + `supervisorRoles`), `dataSchema`, `start`, `states` (`type`, `subType`, `step`, `view`, `actors`, `onEntry`, `onExit`, `transitions`).
   - **`dataSchema`**: Declares the JSON Schema for the workflow's `Data` document. Both frontends automatically generate the **workflow instance table columns** (`/workflows/{key}`) from `dataSchema.properties` in property order.
     - Set `"x-list": false` on any property you want to hide from the list table while keeping it in the schema.
     - Use `"x-mask": "tckn" | "phone" | "iban" | "credit-card"` for automatic formatting/masking and `"x-roles": ["Admin"]` for field-level role masking.
   - Expressions in `rule` and `set` tasks use the server-side C# dialect (DynamicExpresso): `data.Amount > 500000`, `user.IsInRole("Manager")`.
   - A new version's access rules apply only to its instances. When changing `roles`, `instanceAccess`, or supervisor roles, check both old and new instances in the list and detail endpoints.

2. **Validation** runs at API startup – fix every reported error; add validator rules in `WorkflowRegistry` for new definition properties (with tests in `tests/Middt.Framework.Tests/Workflow`).

3. **Screens per state (100% Backend-Driven JSON)**:
   - Every `Human` and `Finish` state should specify `"view": { "layout": "<layout-key>" }` pointing to a `kind: "view"` JSON layout in `demo/Middt.Demo.Api/Layouts/` (e.g., `kyc-review.json`, `credit-review.json`, `order-review.json`, `order-result.json`, `risk-detail.json`).
   - Reuse common partial layouts across multiple workflow views with `{ "widget": "layout", "key": "workflow-footer" }` (see [`backend-driven-json-ui.md`](backend-driven-json-ui.md)).
   - **Transition forms**: Define `input.schema` on transitions (`JsonSchemaForm` in Blazor / `WfSchemaFormComponent` in Angular).
     - Dynamic dropdown lookups against another workflow's data:
       ```json
       "CustomerNo": {
         "type": "string",
         "title": "Müşteri",
         "x-lookup": {
           "endpoint": "customer-onboarding/GetListWithModel",
           "valueField": "CustomerNo",
           "displayField": "FullName"
         }
       }
       ```

4. **Tasks**:
   - Built-in tasks: `set`, `setKey` (business key once known), `log`, `http` (respect `AllowedHttpHosts`, no redirects), `publish`, `startWorkflow` (sync or async subflow), `loadWorkflow` (loads another workflow instance's JSON data by key into scope), `fanOut` (parallel execution of multiple branches with `all` or `any` join strategy), and `cache` (`get`/`set`/`remove` with TTL).
   - Each task attempt gets a private copy of instance data; only a successful attempt's changes are retained. External side effects (`http`, `publish`, `startWorkflow`) must be idempotent. Use `WorkflowTaskContext.IdempotencyKey` with a deduplicating receiver; the built-in `http` task sends it as `Idempotency-Key` unless the definition supplies that header.
   - **Record lifecycle** (master data such as `customer-onboarding.json`): don't finish immediately after creation – set the business key with `setKey`, stay in a `Human` `Active` state with update (`UpdateProfile`), risk recalculation (`RecalculateRisk`), or freeze (`FreezeCustomer`) transitions, and expose the active instances as a directory via `WorkflowInstanceDataQuery` (`/api/customer-onboarding/GetListWithModel`).

5. **Partitioning & indexing**:
   - Declare searchable/filterable fields in `dataSchema.properties` (with explicit types: `integer`, `number`, `boolean`, `string`). The engine automatically creates dedicated PostgreSQL partition tables (`WorkflowInstances_{Suffix}`, `WorkflowInstanceData_{Suffix}`, `WorkflowJobs_{Suffix}`, `WorkflowHistory_{Suffix}`, `WorkflowIncidents_{Suffix}`) and typed functional B-Tree indexes + trigram GIN index at startup.
   - See [`workflow-partitioning-and-indexing.md`](workflow-partitioning-and-indexing.md) for architecture, partition-wise joins, and custom indexing patterns.

6. **Verify**:
   - Run `dotnet test --project tests/Middt.Framework.Tests -- --filter-class "*WorkflowDefinitionTests" --filter-class "*UiSampleLayoutTests" --filter-class "*SampleJsonWorkflowTests"`.
   - Click through both frontends (`/workflows/{key}`).

For a transition that accepts secrets, declare each top-level secret property in `input.sensitive`. The engine strips those values from persisted history and pending input, and rejects asynchronous execution of such transitions.
