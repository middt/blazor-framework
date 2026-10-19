---
description: Run the full definition-of-done verification (.NET, contracts, TypeScript, Angular)
---

Run these from the repository root and report each result (with failing output, not guesses):

1. `dotnet build Middt.Framework.slnx -p:ContinuousIntegrationBuild=true` → must report 0 warnings, 0 errors.
2. `dotnet test --project tests/Middt.Framework.Tests` → all green.
3. If anything under `sdk/client/ui-core` changed: `cd sdk/client/ui-core && npm run build && node --test test/`.
4. If anything under `sdk/client/angular`, `sdk/client/ui-core` or `demo/angular-app` changed:
   `cd sdk/client/angular && npx ng build middt-angular` and
   `cd demo/angular-app && npx ng build sample --configuration production` (0 warnings).
5. If a widget spec changed and the schema test fails: `MIDDT_UPDATE_SPEC=1 dotnet test --project tests/Middt.Framework.Tests`
   and review the diff of `spec/ui-layout.schema.json`.
6. Summarize: what passed, what failed, what was skipped and why. Do not commit.

$ARGUMENTS
