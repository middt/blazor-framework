---
description: Add or change a workflow definition with its screens in both frontends
argument-hint: <workflow description>
---

Implement this workflow change: $ARGUMENTS

Follow `docs/ai/add-workflow.md`: JSON definition in `demo/server/Middt.Sample.Api/Workflows/` (new `version` for
breaking changes), prefer `view.layout` JSON layouts for state screens (both frontends for free), otherwise add the Razor
view **and** the Angular view; engine tests with `WorkflowTestHost` for new rules/validation. Then run `/verify`.
