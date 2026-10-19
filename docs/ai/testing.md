# Testing and verification

| Level | Command | Notes |
|---|---|---|
| CI build | `dotnet build Middt.Framework.slnx -p:ContinuousIntegrationBuild=true` | warnings are errors |
| .NET tests | `dotnet test --project tests/Middt.Framework.Tests` | complete xunit suite |
| Contracts | conformance + schema tests (part of the .NET tests) and `node --test` in ui-core | regenerate schema with `MIDDT_UPDATE_SPEC=1` |
| TypeScript | `cd sdk/client/typescript/ui-core && npm run build && node --test test/` | |
| Angular | `cd sdk/client/angular && npx ng build middt-angular`, then `cd demo/client/angular/app && npx ng build sample --configuration production` | 0 warnings |
| Full stack | `docker compose up -d --build` (needs `.env`) | Blazor :8080, Angular :8081, API :8180 |
| E2E | Playwright scripts (see below) | both frontends, same scenarios |

## E2E
The versioned Playwright smoke suite lives in `tests/e2e` and runs against both demo frontends and the PostgreSQL backed
identity service in CI. Run `npm ci && npx playwright install chromium && npm test` in that folder while the Compose stack
is running. It covers authentication, anonymous API behavior, workflow start dialogs, the layout editor, and identity
admin user queries. Extend this suite with full workflow wizard transitions using the stable ids/classes/aria labels
described in nested `AGENTS.md` files.

## Writing tests
- Use the existing hosts (`TestApiHost`, `WorkflowTestHost`, `UiTestHost`, `SpaBffTestHost`, `BlazorTestHost`).
- Prefer behaviour over implementation; for security fixes assert the attack fails (e.g. 403/404, no request forwarded).
- For performance fixes assert the mechanism (e.g. SQL contains parameters, store read count, command count).
- For workflow changes, use `WorkflowTestHost` and its fake clock. Check both the committed instance and `WorkflowJobs`:
  a Busy recovery must retain a usable pending transition; child cancellation must wait for a Busy child; undelivered
  events must survive later state changes. Test list access with multiple definition versions when roles or owner rules
  change. `WorkflowReliabilityTests` contains focused examples.
