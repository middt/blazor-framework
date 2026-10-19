# Gotchas (learned the hard way)

| Symptom | Cause | Fix / rule |
|---|---|---|
| Stored layouts contain invalid JSON | `[JsonExtensionData] JsonObject` serialises nested props wrongly on .NET 10 | `UiNode` uses `UiNodeJsonConverter`; don't reintroduce extension data |
| Expression returns `"\"text\""` / numbers compare false | switch expression typed as `JsonNode` converts primitives back via implicit operators | cast switch arms to `object` |
| Random test failures reading JSON | `JsonObject` materialises lazily, not thread safe | parse per test/thread |
| Number text differs between .NET and TS | .NET "R" switches to exponent at 1E+15/1E-05 | portable rule = ECMAScript `Number.toString`, implemented explicitly in C# |
| Blazor and Angular demos log each other out | cookies ignore ports | unique `MiddtBffOptions.CookieName` per app |
| Menu never shows layouts | `result === 1` check | `ResultEnum.Success = 0` – use `isSuccess()` |
| Workflow B-tree index fragmentation | random UUIDv4 keys | `WorkflowDbContext.NewId()` uses time-ordered UUIDv7 (`Guid.CreateVersion7()`) |
| Recovered workflow is Faulted but cannot retry | Busy claim had no pending command, or required sensitive input was intentionally not persisted | record the pending transition in the claim; use operator remediation for missing sensitive input |
| Workflow event is delivered twice after a restart | a process can stop after the handler runs but before the job is removed | make `IWorkflowEventHandler` idempotent |
| Child state is overwritten during parent cancellation | child may still hold a Busy claim | queue cancellation, wait until the child is no longer Busy, and increment its revision |
| Each search value creates a SQL plan | Dynamic LINQ inlines constants | expression trees with captured values (parameters) |
| Angular app has two Angular instances / DI errors | linked `file:` packages resolve their own node_modules | `preserveSymlinks: true` in the app |
| Angular page doesn't update after `await` | zoneless + OnPush | update signals, not plain fields |
| Blazor circuit crashes after closing a dialog | cancelled task cached in a shared cache | cache-owned tokens, evict canceled/faulted |
| Login retried 3× and took 6.7 s | resilience handler retried POST/429 | retries only for safe methods, never 429 |
| `docker compose up` fails with "Set IDP_BLAZOR_CLIENT_SECRET" (or another variable) | secrets have no defaults | `cp .env.example .env` and fill it |
| Demo login fails with `invalid_client` after changing a client secret in `.env` | the IdP keeps the secret stored on first start | change it in `http://localhost:8083/admin/clients` too, or drop the `middt_identity` database |
| E2E logout test hangs | typed `/Account/Logout` needs confirmation (logout CSRF protection) | navigate from the app (same-origin) or click the button |
| Existing PostgreSQL database with unpartitioned workflow tables | PostgreSQL does not allow in-place `ALTER TABLE ... PARTITION BY` | `WorkflowPartitionManager.MigrateExistingTablesToPartitionedAsync` automatically renames old tables, creates declarative partitioned tables, routes rows into matching partitions, and syncs sequences |
| Edited `Layouts/*.json` or `Workflows/*.json` seed does not update in DB on restart | `LayoutSeedService` and `WorkflowSeedService` only overwrite DB rows when `existing.Version < incoming.Version` | Increment `"version"` in the JSON seed file whenever you modify an existing layout or workflow |
| Infinite recursion when embedding `"widget": "layout"` | Circular layout references (`A` embeds `B` which embeds `A`) | `UiLayoutWidget` (Blazor and Angular) enforces `MaxDepth = 5` |
