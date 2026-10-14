# sdk/client/angular – Angular frontend library (read the root AGENTS.md first)

Workspace with one library: `projects/middt-angular` → package **`@middt/angular`** (ng-packagr). The demo app is
NOT here (`demo/client/angular/app`). Depends on `@middt/ui-core` as a `file:` package – build ui-core first.

```bash
(cd ../typescript/ui-core && npm run build) && npx ng build middt-angular
```

- Structure: `lib/core` (config/`provideMiddt`, `bffFetch` with `X-CSRF`, `AuthService` via `/bff/user`, guards,
  notifications, SignalR), `lib/ui` (layout renderer, widgets, grid, field editor, dialogs, page/host, editor, caches),
  `lib/workflow` (API client token, list/detail/wizard, JSON Schema form, view registry `provideWorkflowViews`).
- Components: standalone, **OnPush**, signals/`computed` (zoneless app – plain field mutation after `await` won't
  re-render). No template function calls that allocate per change detection (use `computed`).
- Contract logic (expressions, formats, URL safety, field validation, API clients) belongs in **ui-core**, not here.
- Keep markup parity with Blazor (`middt-ui-*`, `middt-wf-*`, ids like `ui-form-{field}`, `wf-step-{transition}-{field}`,
  aria labels) – the Angular E2E suites use the same selectors.
- Public API: export new symbols from `src/public-api.ts`; the demo must only import from `@middt/angular`.
- Library is a single entry point today (lazy routes in the app don't shrink `main`); secondary entry points
  (`core/ui/workflow/signalr`) are a known follow-up.
