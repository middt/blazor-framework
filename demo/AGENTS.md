# demo – bike store sample (read the root AGENTS.md first)

Same shape as `sdk/`: `shared/` (Sample.Common, Sample.Views Razor views, ServiceDefaults), `server/Middt.Sample.Api`,
`client/blazor/Middt.Sample.BlazorServer`, `client/angular/{Middt.Sample.Angular (BFF host), app (Angular demo)}`,
`Middt.Sample.AppHost` (Aspire). The demo shows framework usage – keep framework logic in `sdk/`.

- Seeds: `server/Middt.Sample.Api/Layouts/*.json` (layouts, `$schema` → `spec/ui-layout.schema.json`) and
  `Workflows/*.json` (JSONC). Both are validated at startup. Bump a layout's `version` to overwrite the stored copy.
- Sign-in: both BFFs use Middt Identity (`identity/Middt.Identity`, :8083) configured by `server/identity.json` (seed).
  Demo user `test@test.com` / `test` has roles `manager`, `ui-admin`, `idp-admin`; `user@test.com` / `test` has none.
  Writes on CRUD controllers need `manager` (`SampleCrudController`); records with a lifecycle workflow
  (`ChangedThroughWorkflows`: Store, Staff, Category, Product, Customer, plus read-only ViewOrder and Stock) refuse
  insert/update/delete with 403. Workflows use `instanceAccess` owner mode (owner = the IdP `sub`, managers see all).
- Workflow chain (logical order, linked with `startWorkflow` / `SubFlow`): store-opening → staff-onboarding,
  category-request, product-launch → stock-replenishment, customer-onboarding, order-approval (v3; v1/v2 stay for
  running instances) → order-fulfillment (→ stock-replenishment subflow for missing stock). README "Running the sample"
  has the table. List layouts (`bike-*`) are read-only grids (`create`/`edit`/`delete: false`, no `form`) with a `link`
  button to `/workflow/<key>?start=1`; lifecycle lists add a "Durum" column, a `status` filter (Aktif/Pasif) and the row
  action "Değişiklik / kapatma" → `/workflow/<key>?start=1&key=<entity>-{{ row.<id> }}&<id>={{ row.<id> }}`.
- **Record lifecycle** (store-opening, staff-onboarding, category-request, product-launch, customer-onboarding v2; v1
  files stay for running instances): one instance per record for its whole life.
  - Start input = optional record id (`storeId` …, `x-lookup`); `entry` routes `adopt` → `loading`, else the create flow.
  - After the create task an automatic hop goes to `loading`: `entity-load` (values into `data.<entity>`),
    `setKey` `<entity>-<id>` (never in the hop of a writing task – a failure would roll its marker back), copy to
    `data.change` (prefill of the inline `request-change` form, whose `set` replaces `data.change` with `input`).
  - States: `active` "Aktif" (`request-change`, `request-close`) → `change-approval` (`approve-change`, `reject-change`,
    `withdraw-change`) → `updating` (`entity-update`, idempotent, uniqueness rules) → `loading`;
    `close-approval` (`approve-close`, `reject-close`, `withdraw-close`) → `closing` (`entity-update` sets `active` false) →
    Finish `closed` "Kapatıldı"; rule violations → `change-fix` (`back-to-record`); passive adopted records →
    `inactive` "Pasif" (`request-reactivate` → `reactivate-approval` → `reactivating` → `loading`); missing record →
    Finish `not-found`. Separate review objects per request (`changeReview`, `closeReview`, `reactivateReview`).
  - Closing is a soft delete (`active` flag; staffs `active` tinyint): lookups (`getall`) list active records only,
    `getitems` all (filter `status`). Tasks creating new references (`entity-create`, `db-query`,
    `entity-update`) reject passive records ("… pasif durumda."); stock
    movements of existing orders still work. The `active` columns come from `BikeStoresSchemaUpgrade` (startup, idempotent).
- Workflow definitions use only the framework's general `entity-*`, `db-*`, `set`, `publish`, and `startWorkflow`
  tasks (plus plugin messaging tasks); the demo registers no custom task. `db-sql` performs conditional stock updates,
  `db-query` reads prices and stock, and `entity-create` can save related order items in the same operation.
  Database writes happen outside the engine transaction and may be retried. Keep writes idempotent where possible,
  with a data marker and one write per state. User-fixable rule violations go in `data.error` and route to a
  correction state (`bike-task-error`); unexpected errors fault the instance.
- Input schemas use `x-lookup` (entity = controller: Store, Staff, Customer, Product, Category, Brand) for selects
  (active records only); Customer and Product lists are sorted newest first so new records appear in the first lookup
  page (`getall` is capped at 1000).
- Tests: `tests/…/Workflow/BikeStoreWorkflowTests` runs the chain on SQLite – extend it with every workflow change.
- The `test-*` flows exercise framework features. `test-retry-tasks` uses a deliberately failing `db-query` with
  `onError` to show retry followed by `Continue`, then a general `set` task records the outcome.
- Both frontends must show the same features: a change to a screen/workflow view usually needs Blazor
  (`Sample.Views` Razor views) **and** Angular (`app/src/app/views`) work.
- Running: `docker compose up -d --build` with `.env` (see `.env.example`). Rebuild only what changed:
  `docker compose up -d --build middt-sample-angular`. Database seed: `data/database.sql` (UTF-16).
- Angular demo consumes `@middt/angular` and `@middt/ui-core` as packages (`file:` deps + `preserveSymlinks`);
  `npm run build:sdk` builds both.
