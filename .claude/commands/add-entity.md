---
description: Add a CRUD / directory screen end to end (JSON-backed workflow or EF Core entity + JSON layout in both frontends)
argument-hint: <ScreenOrEntityName> [details]
---

Add the CRUD / directory screen described here: $ARGUMENTS

Follow `docs/ai/add-crud-entity.md` and `docs/ai/backend-driven-json-ui.md`:
- Prefer **Option A (Zero C# Code)**: back the `grid` layout with a workflow definition's `dataSchema` (`endpoint: "<workflow-key>"`, copy `customer-directory.json` in `demo/server/Middt.Sample.Api/Layouts/`).
- Use **Option B** only if an explicit EF Core relational table + `BasePolicyCrudController` is requested.
Then run `/verify` and list the URLs to try in both frontends (`/ui/<key>`).
