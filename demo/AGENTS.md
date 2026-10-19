# demo – workflow & server-driven UI sample (read the root AGENTS.md first)

Same shape as `sdk/`: `shared/` (ServiceDefaults), `server/Middt.Sample.Api`,
`client/blazor/Middt.Sample.BlazorServer`, `client/angular/{Middt.Sample.Angular (BFF host), app (Angular demo)}`,
`Middt.Sample.AppHost` (Aspire). The demo shows framework usage – keep framework logic in `sdk/`.

- Seeds: `server/Middt.Sample.Api/Layouts/*.json` (layouts, `$schema` → `spec/ui-layout.schema.json`) and
  `Workflows/*.json` (JSONC). Both are validated at startup. Bump a layout's `version` to overwrite the stored copy.
- Everything is 100% backend-driven from JSON (see [`docs/ai/backend-driven-json-ui.md`](../docs/ai/backend-driven-json-ui.md)):
  - Sidebar workflow menu items (`icon`, `title`, `/workflows/{key}`) come from `GET /api/workflows/definitions`.
  - Instance list grid columns come from `dataSchema` in `Workflows/*.json` (use `"x-list": false` to hide a field from the list table).
  - Start and transition forms come from `input.schema` in `Workflows/*.json` (supports `"x-lookup"` and `"x-mask"`).
  - State detail views come from `Layouts/*.json` (`kind: "view"`, referenced via `"view": { "layout": "..." }`) and are editable at runtime in `/admin/layouts`.
  - Reusable partial layouts (e.g. `workflow-footer.json`) can be embedded inside any layout via `{ "widget": "layout", "key": "workflow-footer", "data": "<optional-expr>" }`.
  - There are NO hardcoded Razor or Angular views in the demo.
- Sign-in: both BFFs use Middt Identity (`identity/Middt.Identity`, :8083) configured by `server/identity.json` (seed).
  Demo user `test@test.com` / `test` has roles `manager`, `ui-admin`, `idp-admin`; `user@test.com` / `test` has none.
- Running: `docker compose up -d --build` with `.env` (see `.env.example`). Rebuild only what changed:
  `docker compose up -d --build middt-sample-angular`.
- Angular demo consumes `@middt/angular` and `@middt/ui-core` as packages (`file:` deps + `preserveSymlinks`);
  `npm run build:sdk` builds both.
