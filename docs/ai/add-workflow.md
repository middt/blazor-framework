# Playbook: add or change a workflow

Definitions are JSON(C) files in `demo/Middt.Demo.Api/Workflows/` (`customer-onboarding.json`, `credit-allocation.json`, and `order-payment-orchestration.json` are full examples: `dataSchema`, `icon`, rules, subflows, `fanOut`, `loadWorkflow`, `cache`, roles, timers, and backend-driven `view.layout` screens; `invoice-collection.json` shows message-driven `Event` transitions, a `background` task and `history: delta`).

1. **Definition & Dynamic Menu / List Columns**:
   - Top-level properties: `key`, `version` (bump `version` when updating a seeded file so `WorkflowSeedService` updates the DB), `title`, `icon` (Material icon rendered automatically in the sidebar menu of both frontends), `roles`, optional `instanceAccess` (`owner` + `supervisorRoles`), `dataSchema`, optional `history` (`full` default, `delta`, `none`) and `historySnapshotEvery` (delta only, default 20), `start`, `states` (`type`, `subType`, `step`, `view`, `actors`, `onEntry`, `onExit`, `transitions`).
   - **History**: keep `full` unless the history grows large. `delta` stores mostly the change per transition (a JSON Patch) with a snapshot at the first and final row, every `historySnapshotEvery` rows and when a patch saves nothing. `none` writes no history rows – the instance data is still saved on every transition; use it for one-shot or high-volume flows whose audit lives elsewhere. What a row looks like, how the patch is produced and how to read versions back: [README – History modes](../../README.md#history-modes-and-how-json-patch-is-used).
   - If no external system sends the messages yet, give the same step a manual twin (as `record-payment` next to the `payment-received` event transition in `invoice-collection.json`) so it can be driven by hand.
   - **`dataSchema`**: Declares the JSON Schema for the workflow's `Data` document. Both frontends automatically generate the **workflow instance table columns** (`/workflow/{key}`) from `dataSchema.properties` in property order.
     - Set `"x-list": false` on any property you want to hide from the list table while keeping it in the schema.
     - Set `"x-search": true` (or `"text" | "select" | "range" | "min" | "max" | "gte" | "lte" | "date-range"`) on filterable properties, and `"x-date-search": true` on root `dataSchema` for creation date range filters.
     - Set `"x-index": true` (or `false`) to explicitly control partition B-Tree functional index creation.
     - Use `"x-mask": { "pattern": "last4" | "email" | "***", "exceptRoles": ["manager", "auditor"] }` for automatic field masking and `"x-roles": ["manager"]` for field-level role visibility.
   - Expressions in `rule` and `set` tasks use the server-side C# dialect (DynamicExpresso): `data.amount > 500000`, `user.IsInRole("manager")`, plus JSON array helpers (`data.partialPayment.Sum("amount")`, `Count`, `Any`, `Last`, `Remove`, `Where`, `WhereNot`, and negative indexing `[-1]`).
   - A new version's access rules apply only to its instances. When changing `roles`, `instanceAccess`, or supervisor roles, check both old and new instances in the list and detail endpoints.

2. **Validation** runs at API startup – fix every reported error; add validator rules in `WorkflowRegistry` for new definition properties (with tests in `tests/Middt.Framework.Tests/Workflow`).

3. **Screens per state (100% Backend-Driven JSON)**:
   - Every `Human` and `Finish` state should specify `"view": { "layout": "<layout-key>" }` pointing to a `kind: "view"` JSON layout in `demo/Middt.Demo.Api/Layouts/` (e.g., `kyc-review.json`, `credit-review.json`, `order-review.json`, `order-result.json`, `risk-detail.json`, `invoice-detail.json`, `invoice-result.json`).
   - Reuse common partial layouts across multiple workflow views with `{ "widget": "layout", "key": "workflow-footer" }` (see [`backend-driven-json-ui.md`](backend-driven-json-ui.md)).
   - **Transition forms**: Define `input.schema` on transitions (`JsonSchemaForm` in Blazor / `MiddtJsonSchemaFormComponent` in Angular). Use `"into": "items[]"` to append a validated object to an array in `data`.
     - **Partial updates**: `"input": { "format": "jsonPatch", "into": "<owned subtree>", "schema": { … } }` makes the body an RFC 6902 JSON Patch on `data.<into>` (both `into` and `schema` are required; the schema validates the subtree after the patch; a failed `test` is 409). Both frontends prefill the form with the current values and send only the difference. Not on the start transition, event transitions, `sensitive` inputs or `?async=true`. Example: `update-billing-contact` in `invoice-collection.json`; details in the [README](../../README.md#updating-data-with-a-json-patch-inputformat-jsonpatch).
     - Dynamic dropdown lookups against another workflow's data (`x-lookup`) or an array in the current instance (`x-enumFrom`):
       ```json
       "customerKey": {
         "type": "string",
         "title": "Müşteri",
         "x-lookup": {
           "entity": "CustomerOnboarding",
           "valueField": "key",
           "textField": "fullName"
         }
       }
       ```

4. **Triggers**:
   - `Manual` (buttons), `Automatic` (`rule` / `default`), `Scheduled` (`after` / `at`) and `Event`: `"trigger": "Event", "event": "payment.received"` runs when a message of that name is delivered to the instance (`WorkflowEngine.DeliverMessageAsync`, `POST /api/workflows/{key}/instances/{id}/messages/{name}` or `POST /api/workflows/{key}/messages/{name}?key=<business key>`). The message body is the transition `input`; several event transitions may listen for one message and the first whose `rule` holds (it can read `input`) runs. Event transitions are never shown as buttons. The sender needs the transition `roles`; `?bufferFor=PT10M` keeps a message the instance cannot take yet. The start transition cannot be an event transition.

5. **Tasks**:
   - Built-in tasks: `set`, `setKey` (business key once known), `log`, `http` (respect `AllowedHttpHosts`, no redirects), `publish`, `startWorkflow` (sync or async subflow), `loadWorkflow` (loads another workflow instance's JSON data by key into scope), `fanOut` (parallel execution of multiple branches with `all` or `any` join strategy), `cache` (`get`/`set`/`remove` with TTL), and `operation` (dispatches an explicitly registered domain operation).
   - Each task attempt gets a private copy of instance data; only a successful attempt's changes are retained. External side effects (`http`, `publish`, `startWorkflow`) must be idempotent. Use `WorkflowTaskContext.IdempotencyKey` with a deduplicating receiver; the built-in `http` task sends it as `Idempotency-Key` unless the definition supplies that header.
   - **Long calls**: mark a state `onEntry` task `"background": true` (not in finish or SubFlow states, not `setKey`). The state commits first and the instance stays Active; the task runs as a job outside the lock, its data changes are merged when it finishes, then the remaining `onEntry` tasks and automatic transitions run. A failure follows the task's `onError` (`Fault` → `retry` runs it again). Background tasks receive no transition `input`.
   - **Record lifecycle** (master data such as `customer-onboarding.json`): don't finish immediately after creation – set the business key with `setKey`, stay in a `Human` `Active` state with update (`add-note`, `update-income`), risk recalculation, or close (`close-account`) transitions, and expose the active instances as a directory via `WorkflowInstanceDataQuery` (`/api/v1/customer-onboarding/getitems`).

6. **Partitioning & indexing**:
   - Declare searchable/filterable fields in `dataSchema.properties` (with explicit types: `integer`, `number`, `boolean`, `string` and `"x-search": true` or `"x-index": true`). The engine automatically creates dedicated PostgreSQL partition tables (`WorkflowInstances_{Suffix}`, `WorkflowInstanceData_{Suffix}`, `WorkflowJobs_{Suffix}`, `WorkflowHistory_{Suffix}`, `WorkflowIncidents_{Suffix}`) and selective typed functional B-Tree indexes + global `jsonb_path_ops` GIN index at startup.
   - See [`workflow-partitioning-and-indexing.md`](workflow-partitioning-and-indexing.md) for architecture, partition-wise joins, and custom indexing patterns.

7. **Verify**:
   - Run `dotnet test --project tests/Middt.Framework.Tests -- --filter-class "*WorkflowDefinitionTests" --filter-class "*UiSampleLayoutTests" --filter-class "*SampleJsonWorkflowTests"`.
   - Click through both frontends (`/workflow/{key}`).

For a transition that accepts secrets, declare each top-level secret property in `input.sensitive`. The engine strips those values from persisted history and pending input, and rejects asynchronous execution of such transitions.
