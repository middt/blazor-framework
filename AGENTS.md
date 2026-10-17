# AGENTS.md – Middt Framework

Instructions for AI coding assistants (Claude Code, Codex, Cursor, Copilot, …) and for humans working with them.
This file is the single source of truth; tool specific files (`CLAUDE.md`, `.github/copilot-instructions.md`,
`.cursor/rules/`) only point here. Area specific rules live in nested `AGENTS.md` files next to the code – read the one
of the folder you work in. Task playbooks: [`docs/ai/`](docs/ai/README.md).

## What this is

A .NET 10 framework for line-of-business apps with **two interchangeable frontends** over one backend:

- **Backend**: REST API (controllers + repositories on EF Core), JSON-defined **workflow engine**, **server driven UI**
  (JSON layouts stored in the DB, editable at runtime).
- **Frontends**: **Blazor Server** (Radzen) and **Angular 22** (Material). Both sit behind a **BFF** (cookie session,
  server side tokens, `/api` + `/hubs` proxy). The browser never holds an API token.
- **Identity** (`identity/`, a service, not part of the SDK): OAuth 2.0 / OIDC server (OpenIddict) whose
  sign-in/registration/reset/MFA flows are workflow journeys, with a Blazor UI + admin console. The demos' BFFs sign in
  there; the API validates its tokens.
- **Contracts shared by .NET and TypeScript**: layout model + widget specs, the **portable expression language**
  (`{{ }}` templates, `visibleWhen`) with a shared conformance suite in `spec/`.

## Repository map

```
sdk/                              the framework (NuGet + npm packages)
  shared/                         Model · Expressions · Ui.Abstractions        contracts, NO UI technology / ASP.NET
  server/                         Common · Data · Api · Workflow · Ui.Server    backend, NO frontend dependencies
    plugins/                      Plugin.Redis · Plugin.Rebus · Plugin.SyncfusionExport
  client/
    dotnet/                       Middt.Framework.Client                        typed API clients (.NET)
    typescript/ui-core/           @middt/ui-core                                TS twin of the contracts + API clients
    bff/                          Middt.Framework.Bff                           cookie session, login pages, YARP proxy
    blazor/                       Blazor.Web · Workflow.Razor                   Blazor frontend
    angular/                      @middt/angular (library workspace)            Angular frontend
identity/Middt.Identity           OAuth2/OIDC server – ONE runnable service (not SDK), configured by appsettings (docs/identity.md)
demo/                             bike store sample (same shape: shared / server / client)
  server/Middt.Sample.Api         API + Layouts/*.json + Workflows/*.json seeds
  server/identity.json            demo settings of Middt Identity (:8083) – both demos sign in there
  client/blazor/…BlazorServer     Blazor demo (:8080)
  client/angular/                 Middt.Sample.Angular (BFF host, :8081) + app/ (Angular demo app)
  Middt.Sample.AppHost            Aspire orchestration
spec/                             expressions.conformance.json · ui-layout.schema.json (generated)
tests/Middt.Framework.Tests       all .NET tests (xunit.v3)
docs/                             architecture.md, ai/ playbooks
```

Layering is enforced by `tests/…/Architecture/LayeringTests.cs` (assembly references). Never make `sdk/shared` depend on
ASP.NET/UI, `sdk/server` depend on any client/frontend package, or a frontend depend on Data/Api/Workflow/Ui.Server.

## Commands

```bash
# .NET (repo root)
dotnet build Middt.Framework.slnx                                   # dev build
dotnet build Middt.Framework.slnx -p:ContinuousIntegrationBuild=true # CI build: warnings are ERRORS – must be 0
dotnet test --project tests/Middt.Framework.Tests                   # all tests (xunit.v3, Microsoft.Testing.Platform)
dotnet test --project tests/Middt.Framework.Tests -- --filter-class "*WorkflowEngineTests"
MIDDT_UPDATE_SPEC=1 dotnet test --project tests/Middt.Framework.Tests  # regenerate spec/ui-layout.schema.json

# TypeScript / Angular (order matters: ui-core → library → app)
(cd sdk/client/typescript/ui-core && npm ci && npm run build && node --test test/)
(cd sdk/client/angular && npm ci && npx ng build middt-angular)
(cd demo/client/angular/app && npm ci && npx ng build sample --configuration production)

# Full stack (needs .env – copy .env.example and fill it)
docker compose up -d --build        # Blazor :8080 · Angular :8081 · IdP :8083 · API :8180/scalar (Development) · login test@test.com / test
```

## Definition of done (every change)

1. CI build has **0 warnings** (`-p:ContinuousIntegrationBuild=true`); analyzers are on (`latest-recommended`).
2. `dotnet test` green; new behaviour has tests next to the existing ones (same folder/style).
3. If TS/Angular touched: ui-core tests + both Angular builds clean (production build without warnings).
4. If a **contract** changed (layout model, widget spec, expression semantics, API DTO): change **both** .NET and
   TypeScript, update `spec/` (conformance cases / regenerated schema), and both frontends.
5. If a user-visible behaviour or default changed: README (and `docs/architecture.md` when structural).
6. Do **not** commit, push, or run `docker compose down -v` unless the user asks.

## Conventions

- **C#**: file-scoped namespaces, primary constructors, collection expressions, `CancellationToken` everywhere, async
  all the way, `CultureInfo.InvariantCulture` for machine formats, nullable enabled. XML doc comments on public APIs
  (concise, explain *why*). Central package versions in `Directory.Packages.props` – never put versions in csproj.
- **Namespaces don't always match folders** (historic): e.g. `Middt.Framework.Common.Model.Data` lives in
  `sdk/shared/Middt.Framework.Model`, `ApiSettings` is `Middt.Framework.Api.Configuration.Model`. Find types with grep,
  don't guess.
- **Result envelope**: APIs return `BaseResponseDataModel<T>` `{ result, messageList, count, data }`; errors are RFC 9457
  ProblemDetails. **`ResultEnum.Success = 0`**, Info = 1, Error = -1 (TS: use `isSuccess()` from ui-core).
- **UI texts are Turkish** and identical in Blazor and Angular ("Kaydet", "Bu alan zorunludur.", …). Keep ids, CSS
  classes (`middt-ui-*`, `middt-wf-*`), `data-*` attributes and aria labels stable – both E2E suites depend on them.
- **TypeScript (ui-core)**: erasable syntax only (no enums/namespaces/parameter properties), relative imports with
  `.ts` extension, zero runtime dependencies. **Angular**: standalone components, signals, `ChangeDetectionStrategy.OnPush`,
  zoneless; library code must not import from the demo app.
- **Security defaults are deliberate** (see README "Secure defaults"): don't weaken CSP, CSRF header, proxy deny list,
  page size clamps, hidden workflow errors, OpenAPI-in-Development-only, etc. without being asked.

## Gotchas that cost time before

- `UiNode` uses a custom `UiNodeJsonConverter` – `[JsonExtensionData] JsonObject` writes invalid JSON on .NET 10.
- C# switch expressions returning both primitives and `JsonNode` silently convert primitives back to `JsonValue`
  (implicit operators) – cast arms to `object`.
- `JsonObject` lazily materialises and is **not thread-safe for reads**: parse per test/thread.
- Number text in expressions follows ECMAScript `Number.prototype.toString`, not .NET "R".
- Browsers don't separate cookies by port: each BFF app on the same host needs its own `CookieName`.
- SQL Server sorts `uniqueidentifier` by bytes 10–15: use the engine's sequential id generator, not random/v7 GUIDs.
- Seeds (`demo/server/…/Layouts`, `Workflows`) are validated at startup – an invalid file stops the API.
- E2E (Playwright) suites live in `tests/e2e`; see `docs/ai/testing.md`.
- Identity admin pages run on SQL Server without MARS: materialize an `IAsyncEnumerable` from OpenIddict/EF before
  querying per row (SQLite in the tests does not catch "open DataReader").
- Radzen grids with `LoadData` lose the initial load across prerendering: call `grid.Reload()` in
  `OnAfterRenderAsync(firstRender)`.
