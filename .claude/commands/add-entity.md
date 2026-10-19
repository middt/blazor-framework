---
description: Add a CRUD / directory screen end to end (JSON-backed workflow + JSON layout in both frontends)
argument-hint: <ScreenOrEntityName> [details]
---

Add the CRUD / directory screen described here: $ARGUMENTS

Follow `docs/ai/add-crud-entity.md` and `docs/ai/backend-driven-json-ui.md`:
- Back the `grid` layout with a workflow definition's `dataSchema` (`entity: "<WorkflowKey>"`, copy `customer-directory.json` in `demo/Middt.Demo.Api/Layouts/`).
- Run `/verify` and list the URLs to try in both frontends (`/ui/<key>`).
