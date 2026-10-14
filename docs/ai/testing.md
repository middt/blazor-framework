# Testing and verification

| Level | Command | Notes |
|---|---|---|
| CI build | `dotnet build Middt.Framework.slnx -p:ContinuousIntegrationBuild=true` | warnings are errors |
| .NET tests | `dotnet test --project tests/Middt.Framework.Tests` | ~1,100 tests, < 1 min |
| Contracts | conformance + schema tests (part of the .NET tests) and `node --test` in ui-core | regenerate schema with `MIDDT_UPDATE_SPEC=1` |
| TypeScript | `cd sdk/client/typescript/ui-core && npm run build && node --test test/` | |
| Angular | `cd sdk/client/angular && npx ng build middt-angular`, then `cd demo/client/angular/app && npx ng build sample --configuration production` | 0 warnings |
| Full stack | `docker compose up -d --build` (needs `.env`) | Blazor :8080, Angular :8081, API :8180 |
| E2E | Playwright scripts (see below) | both frontends, same scenarios |

## E2E
The Playwright suites used so far (screens, layout editor, workflow wizard, security – one set per frontend) are kept
outside the repo. When adding them, place them under `tests/e2e` with their own `package.json`, a `BASE` URL per
frontend and shared helpers; they rely on the stable ids/classes/aria labels described in the nested `AGENTS.md` files.
Known data dependency: the Blazor workflow suite's "v1 instance" check scans only the first 50 list rows.

## Writing tests
- Use the existing hosts (`TestApiHost`, `WorkflowTestHost`, `UiTestHost`, `SpaBffTestHost`, `BlazorTestHost`).
- Prefer behaviour over implementation; for security fixes assert the attack fails (e.g. 403/404, no request forwarded).
- For performance fixes assert the mechanism (e.g. SQL contains parameters, store read count, command count).
