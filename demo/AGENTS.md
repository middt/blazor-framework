# demo – bike store sample (read the root AGENTS.md first)

Same shape as `sdk/`: `shared/` (Sample.Common, Sample.Views Razor views, ServiceDefaults), `server/Middt.Sample.Api`,
`client/blazor/Middt.Sample.BlazorServer`, `client/angular/{Middt.Sample.Angular (BFF host), app (Angular demo)}`,
`Middt.Sample.AppHost` (Aspire). The demo shows framework usage – keep framework logic in `sdk/`.

- Seeds: `server/Middt.Sample.Api/Layouts/*.json` (layouts, `$schema` → `spec/ui-layout.schema.json`) and
  `Workflows/*.json` (JSONC). Both are validated at startup. Bump a layout's `version` to overwrite the stored copy.
- Sign-in: both BFFs use Middt Identity (`identity/Middt.Identity`, :8083) configured by `server/identity.json` (seed).
  Demo user `test@test.com` / `test` has roles `manager`, `ui-admin`, `idp-admin`; `user@test.com` / `test` has none.
  Writes on CRUD controllers need `manager` (`SampleCrudController`); order-approval uses `instanceAccess` owner mode
  (owner = the IdP `sub`).
- Both frontends must show the same features: a change to a screen/workflow view usually needs Blazor
  (`Sample.Views` Razor views) **and** Angular (`app/src/app/views`) work.
- Running: `docker compose up -d --build` with `.env` (see `.env.example`). Rebuild only what changed:
  `docker compose up -d --build middt-sample-angular`. Database seed: `data/database.sql` (UTF-16).
- Angular demo consumes `@middt/angular` and `@middt/ui-core` as packages (`file:` deps + `preserveSymlinks`);
  `npm run build:sdk` builds both.
