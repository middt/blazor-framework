---
description: Add or change a workflow definition with its JSON-driven screens in both frontends
argument-hint: <workflow description>
---

Implement this workflow change: $ARGUMENTS

Follow `docs/ai/add-workflow.md` and `docs/ai/backend-driven-json-ui.md`:
1. JSON definition in `demo/Middt.Demo.Api/Workflows/` (include `icon`, `dataSchema` with `"x-list": false` where needed, and bump `version` when updating a seeded workflow).
2. State screens in `demo/Middt.Demo.Api/Layouts/` (`kind: "view"`) referenced via `"view": { "layout": "<layout-key>" }` (rendered automatically by both Blazor and Angular; embed reusable partial layouts via `{ "widget": "layout", "key": "..." }`).
3. Add or update engine/layout tests (`WorkflowDefinitionTests`, `UiSampleLayoutTests`, `SampleJsonWorkflowTests`). Then run `/verify`.
