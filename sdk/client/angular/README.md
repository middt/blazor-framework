# Middt Angular

Angular 22 frontend of the Middt framework – the counterpart of `Middt.Framework.Blazor.Web`. Both frontends talk to the
same API, render the same JSON layouts and drive the same workflows.

This workspace contains only the library **`@middt/angular`** (`projects/middt-angular`): BFF authentication, layout
renderer + widgets + runtime editor, workflow list/detail/wizard/JSON Schema forms, SignalR, notifications. It is built on
[`@middt/ui-core`](../typescript/ui-core) (consumed as a package) and Angular Material.

The demo app lives in [`demo/client/angular/app`](../../../demo/client/angular/app) and uses both packages exactly like
any application would; it is served by the BFF host [`demo/client/angular/Middt.Sample.Angular`](../../../demo/client/angular/Middt.Sample.Angular)
(cookie session, server side tokens, `/api` + `/hubs` proxy) – the browser never sees a token.

## Build

```bash
(cd ../typescript/ui-core && npm ci && npm run build)    # @middt/ui-core → dist (dependency of the library)
npm ci && npx ng build middt-angular                      # @middt/angular → dist/middt-angular (ng-packagr)
```

Run the demo: `docker compose up --build` from the repository root → <http://localhost:8081> (test@test.com / test), or
for live reload `cd demo/client/angular/app && npm ci && npx ng serve sample` (http://localhost:4200, proxies to the BFF
on :8081). The demo links the packages with `file:` dependencies and `preserveSymlinks`, so a rebuilt library is picked
up without reinstalling.

## Using the library in an app

New applications should import from `@middt/angular/core`, `@middt/angular/ui`,
`@middt/angular/workflow`, and `@middt/angular/signalr`. The root `@middt/angular`
entry point remains available for existing applications. Register workflow views in
a lazy workflow route so the workflow package stays out of the initial bundle.

```ts
// app.config.ts
providers: [
  provideRouter(routes, withComponentInputBinding()),
  provideMiddt({ locale: 'tr-TR', apiVersion: '1.0' }),           // BFF defaults: /api, /bff/user, /Account/Login
  provideWorkflowViews([{ name: 'order-review', kind: 'state', component: OrderReviewView }]),
  provideMiddtUiWidgets({ name: 'chart', component: ChartWidget, interactive: false, description: 'Chart.' }),
]

// routes
{ path: 'ui/:key', component: MiddtLayoutPageComponent, canActivate: [middtAuthGuard] },
{ path: 'ui-admin', component: MiddtLayoutEditorComponent, canActivate: [middtRoleGuard('ui-admin')] },
```

- **Layouts**: `<middt-layout-host layoutKey="order-escalated" [variables]="{ data, instance }" />` embeds a layout;
  widgets map by name (`MIDDT_UI_WIDGETS`); expressions, templates and formats come from `@middt/ui-core` and behave
  exactly like the .NET side (shared conformance suite).
- **Workflows**: `<middt-workflow-instance-list>` + `<middt-workflow-instance-detail>`; state views resolve in the order
  registered Angular view → JSON layout (`view.layout`) → server HTML (`…/view`) → raw data.
- **Custom widgets / views** must also be known by the API (`MiddtUiOptions.AddWidget`, workflow view names).

