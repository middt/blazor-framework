# identity – Middt Identity, the OAuth 2.0 / OIDC server (read the root AGENTS.md first)

`Middt.Identity` is ONE runnable service (not an SDK package): OpenIddict server + ASP.NET Core Identity + journeys on
the workflow engine (root folders) and the Blazor UI (`Ui/`: account pages, journey views, `/admin`). Deployment
specifics are configuration (`appsettings`, env, `Identity:ConfigFile`) – never put demo data or secrets in the
project; the demo's settings live in `demo/server/identity.json`. Full reference: `docs/identity.md`.

- Journeys (`Journeys/auth-*.json`) are workflow definitions; passwords and codes are `input.sensitive` (never
  persisted). Every journey call is bound to the browser (`middt.idp.journey` cookie, hashed in the instance) and a
  succeeded journey is consumed once (`/account/journey/{id}/complete` signs in).
- Journey definitions use the framework's general `operation` task. Identity operations are allowlisted in
  `IdentityJourneyOperationDispatcher`; keep password, code and OIDC rules in the identity service.
- Account pages are static SSR (they set cookies) – keep `[ExcludeFromInteractiveRouting]`; admin pages are
  interactive and open a DI scope per operation. Keep the ids `Input_Username` / `Input_Password` (E2E).
- SQL Server has no MARS in the demo: materialize OpenIddict/EF async streams before per-row queries. Tests use SQLite
  and do not catch this – check the admin pages against the compose stack.
- Discovery endpoints are rebased on the configured issuer (`IssuerEndpointRebaser`): relying parties fetch it over an
  internal URL but browsers must get the public one.
- CSP of the IdP has no `form-action` on purpose (redirect to the client after a form post); keep `frame-ancestors 'none'`.
- Tests: `tests/…/Identity` (`IdentityTestHost`, protocol + journey tests) and `tests/…/Identity/Ui`.
