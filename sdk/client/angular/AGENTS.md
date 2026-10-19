# sdk/client/angular – Angular frontend library (read the root AGENTS.md first)

Workspace with one library: `projects/middt-angular` → package **`@middt/angular`** (ng-packagr). The demo app is
NOT here (`demo/client/angular/app`). Depends on `@middt/ui-core` as a `file:` package – build ui-core first.

```bash
(cd ../typescript/ui-core && npm run build) && npx ng build middt-angular
```

- Structure: `core/src` (config/`provideMiddt`, `bffFetch` with `X-CSRF`, `AuthService` via `/bff/user`, guards,
  notifications), `ui/src` (layout renderer, display widgets, `UiGridWidget`, `UiRecordWidget`, `UiLayoutWidget` for
  nested `"widget": "layout"`, field editor, dialogs, layout editor), `workflow/src` (API client token,
  list/detail/wizard, JSON Schema form, optional view registry `provideWorkflowViews`), `signalr/src`.
- Components: standalone, **OnPush**, signals/`computed` (zoneless app – plain field mutation after `await` won't
  re-render). No template function calls that allocate per change detection (use `computed`).
- Contract logic (expressions, formats, URL safety, field validation, API clients) belongs in **ui-core**, not here.
- Keep markup parity with Blazor (`middt-ui-*`, `middt-wf-*`, ids like `ui-form-{field}`, `wf-step-{transition}-{field}`,
  aria labels) – the Angular E2E suites use the same selectors.
- Public API: export new symbols from `src/public-api.ts`; the demo must only import from `@middt/angular`.
- Library entry points are `core`, `ui`, `workflow`, and `signalr`; the root entry point is a compatibility aggregate.
  Keep workflow view registration in a lazy route so workflow components stay out of `main`.
