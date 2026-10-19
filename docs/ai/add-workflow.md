# Playbook: add or change a workflow

Definitions are JSON(C) files in `demo/server/Middt.Sample.Api/Workflows/` (`order-approval.v2.json` is the full
example: wizard steps, inline transitions, rules, subflow, roles, timers, layouts).

1. **Definition**: `key`, `version` (new version = new file; running instances keep their version), `title`, `roles`,
   optional `instanceAccess` (`owner` + `supervisorRoles`), `start`, `states` (`type`, `subType`, `step`, `view`),
   transitions (`trigger` Manual/Automatic/Scheduled, `rule`, `roles`, `input.schema`, `view.inline`, tasks).
   Expressions here use the server-side C# dialect (DynamicExpresso): `data.amount > 5000`, `user.IsInRole("manager")`.
   A new version's access rules apply only to its instances. When changing `roles`, `instanceAccess` or supervisor roles,
   check both old and new instances in the list and detail endpoints.
2. **Validation** runs at API start – fix every reported error; add validator rules in `WorkflowRegistry` for new
   definition properties (with tests in `tests/…/Workflow`).
3. **Screens per state** – choose one:
   - `"view": { "layout": "my-layout" }` → JSON layout (kind `view`) in `Layouts/`, rendered by both frontends and the
     static HTML endpoint (variables `data`, `instance`). Preferred: frontend independent.
   - `"view": { "component": "order-review" }` → Razor view in `demo/shared/Middt.Sample.Views` **and** an Angular view
     in `demo/client/angular/app/src/app/views` registered with `provideWorkflowViews`.
   - Transition forms: `input.schema` (JSON Schema form in both frontends) or a custom transition view (both frontends).
     Entity selects: `"x-lookup": { "entity": "Store", "valueField": "storeId", "textField": "storeName" }` on a property.
4. **Tasks**: use general `entity-create`, `entity-load`, `entity-update`, `db-query`, `db-sql`, `set`, `log`, `http`
   (respect `AllowedHttpHosts`, no redirects), `publish`, `startWorkflow`, and `setKey` (business key once known).
   `entity-create.children` saves related collection rows in the same database operation; `dateOffsets` adds days to
   a DateTime value, as shown by `order-approval.v3.json`. Custom tasks remain an SDK extension point but are not
   needed by the demo.
   Each task attempt gets a private copy of instance data; only a successful attempt's changes are retained. Database
   writes, messages, child starts and HTTP calls are external side effects and must be idempotent. Use
   `WorkflowTaskContext.IdempotencyKey` with a deduplicating receiver; the built-in `http` task sends it as
   `Idempotency-Key` unless the definition supplies that header. The key is stable across attempts of one command,
   but a later explicit `retry` can generate a different key. Give `startWorkflow` a stable `key` when duplicate
   child starts would be harmful. See `demo/AGENTS.md` for the general database task examples.
   Event handlers run after commit and can be replayed; make them idempotent too.
   **Record lifecycle** (master data): don't finish after the create task – `setKey` `<entity>-<id>`, wait in a Human
   `active` state with change / close requests behind approvals, close = deactivate (soft delete); accept the record id
   as optional start input to adopt existing records. Pattern and rules: `demo/AGENTS.md`, e.g. `category-request.v2.json`.
5. **Verify**: engine tests (`WorkflowTestHost`), then click through both frontends (`/Workflow/OrderApproval`,
   `/workflow/order-approval`). Cover task retry isolation, fault/retry behavior, and any new subflow or event
   delivery path. `test-retry-tasks.json` is a runnable demo of retry followed by `Continue`; `WorkflowReliabilityTests`
   has examples for recovery, versioned access and durable follow-up jobs.
6. **Partitioning & indexing**: Declare searchable/filterable fields in `dataSchema.properties` (with explicit types:
   `integer`, `number`, `boolean`, `string`). The engine automatically creates dedicated PostgreSQL partition tables
   (`WorkflowInstances_{Suffix}`, `WorkflowInstanceData_{Suffix}`, `WorkflowJobs_{Suffix}`, `WorkflowHistory_{Suffix}`,
   `WorkflowIncidents_{Suffix}`) and typed functional B-Tree indexes at startup. See
   [`workflow-partitioning-and-indexing.md`](workflow-partitioning-and-indexing.md) for architecture, partition-wise
   joins, and custom indexing patterns.

For a transition that accepts secrets, declare each top-level secret property in `input.sensitive`. The engine strips
those values from persisted history and pending input, and rejects asynchronous execution of such transitions. A
process interruption can therefore leave a Faulted instance whose sensitive input cannot be reconstructed by the
retry endpoint; plan an operator recovery procedure for that journey.
