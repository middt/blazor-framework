---
description: Add a CRUD entity end to end (API + JSON layout screen in both frontends)
argument-hint: <EntityName> [details]
---

Add the CRUD entity described here: $ARGUMENTS

Follow `docs/ai/add-crud-entity.md` exactly: model + search properties, repository, controller deriving from the demo's
`SampleCrudController`, registration in `Program.cs`, a JSON layout in `demo/server/Middt.Sample.Api/Layouts/` (copy
`bike-category.json`, keep `$schema`), tests for any custom logic. Then run `/verify` and list the URLs to try in both
frontends.
