# Playbook: add or change a workflow

Definitions are JSON(C) files in `demo/server/Middt.Sample.Api/Workflows/` (`order-approval.v2.json` is the full
example: wizard steps, inline transitions, rules, subflow, roles, timers, layouts).

1. **Definition**: `key`, `version` (new version = new file; running instances keep their version), `title`, `roles`,
   optional `instanceAccess` (`owner` + `supervisorRoles`), `start`, `states` (`type`, `subType`, `step`, `view`),
   transitions (`trigger` Manual/Automatic/Scheduled, `rule`, `roles`, `input.schema`, `view.inline`, tasks).
   Expressions here use the server-side C# dialect (DynamicExpresso): `data.amount > 5000`, `user.IsInRole("manager")`.
2. **Validation** runs at API start – fix every reported error; add validator rules in `WorkflowRegistry` for new
   definition properties (with tests in `tests/…/Workflow`).
3. **Screens per state** – choose one:
   - `"view": { "layout": "my-layout" }` → JSON layout (kind `view`) in `Layouts/`, rendered by both frontends and the
     static HTML endpoint (variables `data`, `instance`). Preferred: frontend independent.
   - `"view": { "component": "order-review" }` → Razor view in `demo/shared/Middt.Sample.Views` **and** an Angular view
     in `demo/client/angular/app/src/app/views` registered with `provideWorkflowViews`.
   - Transition forms: `input.schema` (JSON Schema form in both frontends) or a custom transition view (both frontends).
4. **Tasks**: built-ins `set`, `log`, `http` (respect `AllowedHttpHosts`, no redirects), `publish`, `startWorkflow`;
   custom tasks implement `IWorkflowTask` and are registered with `AddTask<T>(name)`.
5. **Verify**: engine tests (`WorkflowTestHost`), then click through both frontends (`/Workflow/OrderApproval`,
   `/workflow/order-approval`).
