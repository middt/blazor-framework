# demo – workflow & server-driven UI demo (read the root AGENTS.md first)

Flat project structure: `Middt.Demo.AppHost` (Aspire), `Middt.Demo.ServiceDefaults`, `Middt.Demo.Api`,
`Middt.Demo.Blazor`, `Middt.Demo.Angular` (BFF host) + `angular-app` (Angular SPA workspace).
The demo shows framework usage – keep framework logic in `sdk/`.

- Seeds: `Middt.Demo.Api/Layouts/*.json` (layouts, `$schema` → `spec/ui-layout.schema.json`) and
  `Workflows/*.json` (JSONC). Both are validated at startup. Bump a layout's `version` to overwrite the stored copy.
- Everything is 100% backend-driven from JSON (see [`docs/ai/backend-driven-json-ui.md`](../docs/ai/backend-driven-json-ui.md)):
  - Sidebar workflow menu items (`icon`, `title`, `/workflows/{key}`) come from `GET /api/workflows/definitions`.
  - Instance list grid columns come from `dataSchema` in `Workflows/*.json` (use `"x-list": false` to hide a field from the list table).
  - Start and transition forms come from `input.schema` in `Workflows/*.json` (supports `"x-lookup"` and `"x-mask"`).
  - State detail views come from `Layouts/*.json` (`kind: "view"`, referenced via `"view": { "layout": "..." }`) and are editable at runtime in `/admin/layouts`.
  - Reusable partial layouts (e.g. `workflow-footer.json`) can be embedded inside any layout via `{ "widget": "layout", "key": "workflow-footer", "data": "<optional-expr>" }`.
  - There are NO hardcoded Razor or Angular views in the demo.
- Workflows: `customer-onboarding`, `credit-allocation` (+ `risk-limit-eval` subflow), `order-payment-orchestration`,
  `sla-escalation`, and `invoice-collection` (payment messages → `Event` transitions with `bufferFor`, a `background`
  ledger call, `history: delta`). Messages have no buttons: send them with the REST endpoints or Postman collection 06.
- Sign-in: both BFFs use Middt Identity (`identity/Middt.Identity`, :8083) configured by `demo/identity.json` (seed).
  Demo user `test@test.com` / `test` has roles `manager`, `ui-admin`, `idp-admin`; `user@test.com` / `test` has none.
- Running: `docker compose up -d --build` with `.env` (see `.env.example`). Rebuild only what changed:
  `docker compose up -d --build middt-sample-angular`.
- Angular demo consumes `@middt/angular` and `@middt/ui-core` as packages (`file:` deps + `preserveSymlinks`);
  `npm run build:sdk` builds both.
