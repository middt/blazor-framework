# Middt Identity (OAuth 2.0 / OpenID Connect server)

`identity/Middt.Identity` is **one ready to run service**: an OAuth 2.0 / OpenID Connect server whose sign-in,
registration, password reset, MFA and consent steps are **authentication journeys** – workflow definitions (JSON) run
by `Middt.Framework.Workflow` – with a Blazor UI (account pages + `/admin` console). It is not part of the SDK: it uses
the SDK (Common, Workflow) and is deployed once; every application (the Blazor and Angular BFFs, APIs) connects to it
over OIDC. Everything deployment specific is **configuration**, not code.

## Project layout (`identity/Middt.Identity`)

| Folder | Contents |
|---|---|
| `Program.cs` | Host: reads the configuration below, maps protocol endpoints, UI, health (`/health`). |
| `Endpoints/`, `Data/`, `Seeding/`, `Messaging/` | Protocol (OpenIddict 7 server: authorization code + PKCE, refresh token, client credentials, end session, revocation, introspection, userinfo), users/roles (ASP.NET Core Identity on EF Core, schema `idp`), seeding, development mailbox. |
| `Journeys/` | `IAuthJourneyService`, workflow tasks and the default journeys (`auth-*.json`, embedded). |
| `Ui/` | Blazor: account pages (static server rendering – they set cookies), journey views, admin pages (interactive server, Radzen). |
| `wwwroot/` | `middt-identity.css`. |
| `Dockerfile` | `docker build -f identity/Middt.Identity/Dockerfile .` (from the repository root). |

Nothing in `sdk/` or the frontends references it (they talk OIDC over HTTP).

## Configuration

Sources: `appsettings.json`, environment variables (`Identity__Issuer` …) and optionally **one extra JSON file** named
by `Identity:ConfigFile` (relative to the content root or absolute; environment variables still override it – keep
secrets there). The demo uses `demo/server/identity.json`.

| Key | |
|---|---|
| `ConnectionStrings:Identity` | SQL Server database (users, clients, tokens, journeys) – required |
| `ConnectionStrings:redis` | optional: shared Data Protection keys (several instances, restarts) |
| `Identity:Issuer` | public URL, e.g. `https://id.example.com/` |
| `Identity:SigningCertificate:{Path,Password,Thumbprint,Encryption*}` | required outside Development (Development uses development certificates) |
| `Identity:Seed:{Roles,Scopes,Clients,Users}` | applied at every start (see below) |
| `Identity:Password:*` | password policy (`PasswordOptions`: `RequiredLength`, `RequireDigit` …) |
| `Identity:JourneyDirectory` | optional directory with journey JSON files overriding the defaults by key |
| `Identity:*` | the options below |

| Option (`Identity:*`) | Default | |
|---|---|---|
| `Issuer` | request host | public URL (`iss`); discovery then advertises every endpoint under it, also when a relying party fetches discovery over an internal address (container network) |
| `Schema` / `WorkflowSchema` | `idp` / `wf_idp` | identity + OpenIddict tables / journey (workflow) tables; ignored by SQLite |
| `AutoCreateSchema` | `false` | creates database, schemas and tables at startup (use migrations in production) |
| `RequireHttps` | on outside Development | OpenIddict transport security |
| `AccessTokenLifetime` / `IdentityTokenLifetime` / `RefreshTokenLifetime` / `AuthorizationCodeLifetime` | 15 min / 15 min / 14 days / 5 min | |
| `SessionLifetime` | 8 h (sliding) | IdP cookie |
| `JourneyLifetime` | 15 min | journeys older than this are unusable (the default journeys also time out after `PT15M`) |
| `MaxJourneyAttempts` | 5 | wrong passwords/codes per journey before `failed` |
| `EmailCodeLifetime` | 10 min | |
| `AdminRole` | `idp-admin` | seeded automatically |
| `AllowRegistration` / `RequireConfirmedEmail` | `true` / `true` | |
| `AuthenticatorIssuerName` | `Middt` | issuer shown in authenticator apps |

Lockout: 5 attempts / 5 minutes; e-mail addresses are unique. The journeys run on an internal workflow engine
(schema `wf_idp`). Only `OpenIddict.Server.AspNetCore` + `OpenIddict.EntityFrameworkCore` are referenced (the
`OpenIddict.AspNetCore` metapackage pulls Polly 7 through the client/validation packages, which clashes with Polly 8).

Seed (`Identity:Seed`), applied idempotently at every start: roles and users are created when missing
(existing users only get missing roles; seeded passwords bypass the password policy), scopes and clients are created or
updated (redirect URIs, permissions, consent type, display name) – **an existing client secret is never replaced**
(rotated secrets survive restarts).

```json
"Identity": {
  "SigningCertificate": { "Path": "certs/idp.pfx", "Password": "…" },          // or "Thumbprint"; optional Encryption* variants
  "Seed": {
    "Roles": [ "manager", "ui-admin" ],
    "Scopes": [ { "Name": "api", "DisplayName": "Demo API", "Resources": [ "middt-api" ] } ],
    "Clients": [ {
      "ClientId": "middt-blazor", "ClientSecret": "…", "DisplayName": "Blazor demo",
      "Type": "confidential",                // or "public" (no secret)
      "ConsentType": "implicit",             // explicit | external | systematic (treated as explicit)
      "RedirectUris": [ "http://localhost:8080/signin-oidc" ],
      "PostLogoutRedirectUris": [ "http://localhost:8080/signout-callback-oidc" ],
      "GrantTypes": [ "authorization_code", "refresh_token" ],   // default; "client_credentials" for services
      "Scopes": [ "openid", "profile", "email", "roles", "offline_access", "api" ]
    } ],
    "Users": [ { "UserName": "test@test.com", "Email": "test@test.com", "Password": "test", "DisplayName": "Test", "Roles": [ "manager", "ui-admin", "idp-admin" ] } ]
  }
}
```

Client permissions are derived: code flow → authorization, end session, `code` response type, PKCE requirement;
always token + revocation; confidential clients also introspection; one scope permission per listed scope.

## Protocol

| Endpoint | Purpose |
|---|---|
| `GET /.well-known/openid-configuration`, `/.well-known/jwks` | discovery, signing keys |
| `GET/POST /connect/authorize` | authorization code flow (PKCE required for every client) |
| `POST /connect/token` | `authorization_code`, `refresh_token` (rotation), `client_credentials` |
| `GET/POST /connect/userinfo` | claims of the access token's user |
| `GET/POST /connect/logout` | RP-initiated logout (`id_token_hint`, `post_logout_redirect_uri`) |
| `POST /connect/revoke`, `POST /connect/introspect` | token revocation / introspection (introspection: confidential clients) |

- Scopes: `openid`, `profile`, `email`, `roles`, `offline_access` and API scopes (demo: `api` → resource/audience `middt-api`).
- Claims: `sub` (user id), `name` (display name / user name), `preferred_username`, `email`, `role` (one per role);
  destinations: the access token gets `sub`, `name`, `preferred_username`, `email`, `role`; the identity token gets
  `sub`, `auth_time` and the others when the matching scope (`profile`, `email`, `roles`) was granted. Userinfo returns
  the same per scope (+ `email_verified`). Client credentials tokens: `sub` = `name` = client id/display name.
- Token requests (`authorization_code`, `refresh_token`) re-load the user: deleted/locked-out users (or unconfirmed ones
  with `RequireConfirmedEmail`) get `invalid_grant`; name, e-mail and roles are refreshed on every refresh.
- Authorization codes are single use (a replay revokes the tokens issued from it); refresh tokens rotate.
- Access tokens are JWTs (signed; **not encrypted**, so APIs validate them with the discovery keys).
- Issuer: `MiddtIdentityOptions.Issuer` (public URL, e.g. `http://localhost:8083/`). In containers, relying parties set
  a **backchannel authority** (e.g. `http://middt-identity:8080/`): server-to-server calls to the issuer are
  rewritten to it (`BackchannelRewriteHandler` in Common), so the issuer stays the public URL everywhere.
- Keys: development certificates in Development (`AddDevelopmentSigningCertificate`), configured certificates
  otherwise (`Identity:SigningCertificate` `Path`/`Password` or `Thumbprint`, optional `EncryptionPath`/
  `EncryptionPassword`/`EncryptionThumbprint`, else the signing certificate also encrypts codes/refresh tokens);
  ephemeral keys only in tests (`UseEphemeralKeys()`). Without a certificate outside Development startup fails.
- Cookies: the IdP session cookie is `middt.idp` (HttpOnly, SameSite=Lax, login path `/account/login`); journey
  binding cookie `middt.idp.journey`. Names differ from the BFF cookies because browsers share cookies across ports.
  The session carries the security stamp: after a password reset or an MFA change other sessions must sign in again
  at their next authorization request (`MiddtIdentitySession.SignInAsync` re-issues the current one).

### Authorize endpoint
1. No valid IdP session (missing, user deleted/locked out, security stamp changed), `prompt=login`, or `max_age`
   exceeded (measured from `auth_time` of the session) → start `auth-login` with `returnUrl` = this authorize request
   (local path + query, form posts converted, **without `prompt`** so it does not loop), client id and scopes; write the
   journey cookie; redirect to `/account/journey/{id}`. `prompt=none` → `login_required`.
2. Signed in, client with `explicit` (or `systematic`, treated as explicit) consent and no permanent authorization for
   the requested scopes, or `prompt=consent` → start the **consent-only path of `auth-login`** (start `UserId` = the
   signed-in user, `forceConsent` for `prompt=consent`). `prompt=none` → `consent_required`. `external` without an
   authorization → `consent_required`.
3. Otherwise: principal with the claims above + scopes/resources, a permanent authorization is reused or created, sign
   in with OpenIddict → redirect with the code.
4. A denied consent comes back as `…&middt_consent_denied=1` → `access_denied` to the client (forging the marker only
   denies your own request).

`/connect/logout` signs out of the IdP cookie without a confirmation page (RP-initiated; OpenIddict only follows a
registered `post_logout_redirect_uri`, else `/`). The UI's `/account/logout` page is the user-facing variant.

## Journeys

A journey is a workflow instance; each state has a `view.component` = journey view name rendered by the Blazor UI;
actions are manual transitions. Inputs that contain secrets use `input.sensitive` (never persisted: not in data,
history, pending input or job payloads). Journey definitions require the role `idp-journey`, which only the journey
service's actor has – a host that also maps the workflow REST endpoints does not expose them. Journey tables live in
the identity database under schema `wf_idp`.

| Journey | States (view) | Actions |
|---|---|---|
| `auth-login` | `credentials` (credentials) → `mfa` (totp) → `consent` (consent) → `authenticated` (Final/Success); `denied` (Final/Success, `consentDenied`); `failed` (Final/Error); `to-register` / `to-forgot-password` (Final/Cancelled, `data.redirect`) | `submit-credentials {userName, password*}`, `submit-totp {code*}`, `approve`/`deny`, `register`, `forgot-password` |
| `auth-register` | `details` (register) → `verify-email` (email-code) → `registered` (Success); `disabled` (Error, registration closed); `failed` | `submit-details {userName, email, displayName, password*}`, `submit-code {code*}`, `resend-code` |
| `auth-reset-password` | `request` (reset-request) → `verify` (email-code) → `new-password` (new-password) → `done` (Success); `failed` | `submit-email {email}`, `submit-code {code*}`, `resend-code`, `submit-password {password*}` |
| `auth-enroll-totp` | `setup` (totp-setup, shows key + otpauth URI/QR) → `enabled` (Success); `failed` | `confirm {code*}` |

`*` = sensitive. Every journey also has `expired` (Final/Timeout, `PT15M`). Routing states without a view (`begin`,
`check-credentials`, `check-mfa`, `check-consent`, `check-details`, `check-code`, `check-password`) hold the automatic
transitions: a task never throws for a wrong password/code – it sets flags and `data.error`, and the routing state sends
the journey back to the step (wrong passwords/TOTP codes still count for the account lockout via `AccessFailedAsync`);
after `MaxJourneyAttempts` failures (`attemptsExceeded`) it moves to `failed`.

- `auth-login`: `begin` routes `consentOnly` (started by the authorize endpoint for a signed-in user) straight to
  `check-consent`. `mfaRequired` = TOTP enabled and an authenticator key exists. `register`/`forgot-password` end the
  journey with `data.redirect` = `/account/register` / `/account/forgot-password` (the UI redirects, keeping `returnUrl`;
  it may as well link to those pages directly). Credentials accept the user name or the e-mail address. With
  `RequireConfirmedEmail` an unconfirmed user gets "E-posta adresiniz henüz doğrulanmadı." (not counted).
- `auth-register`: honours `AllowRegistration` (`disabled`) and `RequireConfirmedEmail` (user created unconfirmed, code
  sent on the way to `verify-email`, confirmed by the code; without it straight to `registered`). Password policy and
  duplicate user name/e-mail are reported in Turkish in `data.error`.
- `auth-reset-password`: the code step follows for **every** address (a code is only mailed to existing users; any code
  fails for unknown ones with the same message); the reset unlocks the account and confirms the e-mail.
- `auth-enroll-totp`: needs `AuthJourneyStart.UserId` (the signed-in user). The new key is kept encrypted (Data
  Protection) in the instance until confirmed; `totpKey`/`totpUri` are only added to the UI data in the `setup` state.
  Completion re-issues the session cookie (enabling MFA changes the security stamp).

Tasks (registered by the package): `idp-verify-credentials` (lockout aware; sets `authenticated`, `userId`,
`userName`, `displayName`, `mfaRequired`), `idp-verify-totp` (`mfaPassed`), `idp-check-consent` (sets
`consentRequired`, `clientName`, `scopeDetails`), `idp-grant-consent` (permanent OpenIddict authorization),
`idp-create-user` (`created`), `idp-send-email-code` / `idp-verify-email-code` (6 digits, only a salted SHA-256 + expiry
stored, single use, `codeSentTo`, max 5 resends; `codeValid`), `idp-reset-password` (`passwordReset`),
`idp-totp-setup` / `idp-enable-totp` (`totpEnabled`).

Applications may override a journey by key (`AddJourneysFromDirectory`) – e.g. add a "terms" step; other keys add
journeys. Keep `roles: ["idp-journey"]`, a start input schema declaring `_bindingHash` (and `returnUrl`, …) and
`sensitive` for secrets.

### Data returned to the UI (`AuthJourneyState`)
`Data` never contains keys starting with `_` (binding hash, code hash/salt/expiry, pending TOTP key, consumed flag),
the submitted `form` or `error`. Useful keys: `userName`, `displayName`, `email`, `clientId`, `clientName`, `scopes`
(names), `scopeDetails` (`[{ name, displayName }]`, Turkish names for the standard scopes), `codeSentTo`,
`failedAttempts`, `redirect`, `consentDenied`, `totpKey` (groups of 4, lower case) and `totpUri` (setup only).
`Errors` = the translated validation errors of the last submit (`"Şifre: Bu alan zorunludur."`) or `data.error`.
`Actions` carry the JSON Schema of their input (Turkish `title`s). A faulted journey (unexpected task error) is
finished with "Beklenmeyen bir hata oluştu…" – sensitive input is not kept, so it cannot be retried: start over.
Start `Data` extras (e.g. a prefilled e-mail) are copied unless they are reserved names or start with `_`.

### Browser binding and completion
`StartAsync` returns a random 256-bit binding secret; the UI stores `{id:N}.{secret}` in the `middt.idp.journey` cookie
(`AuthJourneyCookie.Write`) and the instance stores only its SHA-256. Every call checks it (constant time); unknown,
foreign, expired and consumed journeys all return `null`. When a journey succeeds, the UI redirects to
`GET /account/journey/{id}/complete`, which calls `CompleteAsync` once (a compare-and-swap on the instance revision marks
it consumed; within `JourneyLifetime` after completion), signs the user into the IdP cookie (login/register; TOTP
enrollment re-issues it; reset password and consent-only do not), deletes the journey cookie and redirects to the
journey's local `returnUrl` (e.g. back to `/connect/authorize`) or `/`. Without a matching cookie it redirects to
`/account/login`; an already consumed/unknown journey redirects to `/account/journey/{id}`.

### Development mailbox
`DevelopmentMailbox` (registered with `TryAdd` as `IIdentityMessageSender`; resolve `DevelopmentMailbox` for
`/dev/mailbox`) keeps the last 50 messages in memory (`Messages`, most recent first). Register a real sender for
production – a warning is logged when the development mailbox is active outside Development.

## UI (`Ui/`)

- Account pages (static SSR, `[ExcludeFromInteractiveRouting]` – they set cookies): `/account/login` (starts
  `auth-login`, local `returnUrl` only), `/account/register`, `/account/forgot-password`, `/account/journey/{id}`
  (renders the view of the current state, posts the action in `__action`), `/account/logout` (confirm + POST),
  `/account/security` (enroll/disable TOTP, change password), `/account/denied`. A successful post redirects (next step
  or `/complete`); a post with errors renders them directly (200) – non-sensitive fields are refilled, passwords and
  codes never. The credentials view keeps the ids `Input_Username` / `Input_Password` (E2E compatibility).
- Journey views by `view.component`: `credentials`, `totp`, `consent`, `register`, `email-code`, `reset-request`,
  `new-password`, `totp-setup` (inline QR SVG); other states get a generic JSON Schema form. A new journey view is a
  component deriving from `JourneyViewBase`, registered in `Program.cs` with
  `AddMiddtIdentityUi(o => o.AddJourneyView<MyView>("terms"))`.
- Admin (interactive server, Radzen, policy `MiddtIdentityUi.AdminPolicy` = role `MiddtIdentityOptions.AdminRole`,
  default `idp-admin`; signed-in users without it are sent to `/account/denied`): users (search, create, roles,
  lock/unlock, reset MFA, set password), clients (OpenIddict applications: type, secret shown once, redirect URIs,
  permissions, consent type), scopes, sessions (authorizations/tokens, revoke), journeys (instances + history). Every
  operation uses its own DI scope; queries are materialized before per-row lookups (SQL Server without MARS).
- `/dev/mailbox` in Development: messages of the development `IIdentityMessageSender`.
- Security headers – `frame-ancestors 'none'` + `X-Frame-Options: DENY` (login
  and consent cannot be framed), same-origin scripts. `form-action` is deliberately not set: browsers apply it to the
  redirect chain after a form post, and a sign-in ends with a redirect to the client's `redirect_uri`.
- Texts are Turkish, like the rest of the framework.

## Relying parties in this repository

| Party | How |
|---|---|
| BFF (Blazor and Angular demos) | `MiddtBffOptions.Oidc` / configuration `Bff:Oidc` (`Authority`, `ClientId`, `ClientSecret`, `Scopes`, `BackchannelAuthority`, `RequireHttpsMetadata`, `CallbackPath` = `/signin-oidc`, `SignedOutCallbackPath` = `/signout-callback-oidc`): `/Account/Login` challenges the IdP (code + PKCE, `response_mode=query` – the handler omits the parameter because it is the code flow default), the password form post answers 404. The callback stores access/refresh/id token in the server token store (fresh session id, absolute session expiry, previous session of the browser ended) and issues the usual cookie (`ClaimTypes.Name`/`Role`/`NameIdentifier`/`Email` + session id). Refresh and revocation go to the IdP's token/revocation endpoints (`OidcTokenService`, client authentication `client_secret_post`); `POST /Account/Logout` revokes the refresh token, ends the local session and navigates (meta refresh page, CSP `form-action`) to `end_session_endpoint` with `id_token_hint`, back via `/signout-callback-oidc` to `/`. |
| API | `Authentication:Authority` (+ `Audience`, default `middt-api`; `BackchannelAuthority`; `RequireHttpsMetadata`, default outside Development) or `MiddtApiOptions.Authority`: JWT bearer validation with the discovery keys, issuer = authority; `sub` → `ClaimTypes.NameIdentifier`/`Sid`, `name` → `ClaimTypes.Name`, each `role` → `ClaimTypes.Role`, `email` → `ClaimTypes.Email`. `JwtSettings` are optional then; the symmetric `JwtSettings` mode (`BaseTokenController`) stays for apps without an IdP. |

Environment variables (containers): `Bff__Oidc__Authority=http://localhost:8083/`,
`Bff__Oidc__BackchannelAuthority=http://middt-identity:8080/`, `Bff__Oidc__ClientId`, `Bff__Oidc__ClientSecret`,
`Bff__Oidc__RequireHttpsMetadata=false` (plain http outside Development); API: `Authentication__Authority`,
`Authentication__BackchannelAuthority`, `Authentication__Audience`, `Authentication__RequireHttpsMetadata`.

Demo clients: `middt-blazor` (confidential, `http://localhost:8080/signin-oidc`), `middt-angular` (confidential,
`http://localhost:8081/signin-oidc`); scopes `openid profile email roles offline_access api`. Demo users:
`test@test.com` / `test` (roles `manager`, `ui-admin`, `idp-admin`), `user@test.com` / `test` (no roles).

## Demo (:8083)

The demo runs the service with `demo/server/identity.json`: issuer `http://localhost:8083/`, database
`MiddtIdentity` (created at startup), relaxed password policy (demo password `test`), roles, scope `api` →
`middt-api`, clients `middt-blazor` / `middt-angular` and the demo users. Both demos sign in here; the API validates
its access tokens.

| Run | Client secrets | Notes |
|---|---|---|
| `docker compose up -d --build` | `.env`: `IDP_BLAZOR_CLIENT_SECRET`, `IDP_ANGULAR_CLIENT_SECRET` (required) | service `middt-identity`, `demo/server/identity.json` mounted as `Identity:ConfigFile`; BFFs/API reach it as `middt-identity:8080` (`BackchannelAuthority`); development signing keys persist in the `identity-keys` volume |
| Aspire AppHost / `dotnet run --project identity/Middt.Identity` | `development-only-*` in `demo/server/identity.json` and in both BFFs' `appsettings.Development.json` | fixed launch ports: IdP 8083, Blazor `https://localhost:7020`, Angular 8081 |

- The `middt-angular` client uses explicit consent (the consent page appears once per user), `middt-blazor` implicit.
- A client secret is stored on the first start; changing it in `.env` later also requires changing it in
  `/admin/clients` (or dropping the `MiddtIdentity` database).
- Users are identified by the IdP's `sub` (a GUID): workflow instances started with the old demo token belong to the
  old user id and are not listed for the new one (`instanceAccess` owner mode).
- Development certificates are only acceptable for the demo; production configures `Identity:SigningCertificate`,
  HTTPS and its own `Identity:Seed` (at least one user with the `idp-admin` role).
