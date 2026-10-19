# sdk/client/Middt.Framework.Bff – backend-for-frontend (read the root AGENTS.md first)

Shared by the Blazor host (`AddMiddtBlazor` calls `AddMiddtBff`) and SPA hosts (`AddMiddtSpaBff` / `UseMiddtSpaBff`).
This is a **security boundary** – treat every change as security relevant and add tests in `tests/…/Bff`.

- Session: HttpOnly cookie (`MiddtBffOptions.CookieName`, unique per app on a host) → server side
  `IServerTokenStore` (Data Protection encrypted, `IDistributedCache`). Tokens must never be sent to the browser.
- `BffTokenAccessor`: one store read per request (memoised in `HttpContext.Items`), refresh before expiry, single
  flight per session, failed refresh ends the session.
- Proxy (`Proxy/BffApiProxy.cs`, YARP): `/api/**` and `/hubs/**` → `ApiSettings:URL`. Requires `X-CSRF` on non-WebSocket
  requests, same-origin `Origin` for WebSockets, strips cookies, injects the bearer token, blocks
  `ProxyDeniedPaths` (default `token`). Do not add routes that bypass these checks.
- Pages: `/Account/Login` (antiforgery, per-IP + per-username limits, `LocalUrl` return URLs, generic errors) and
  `/Account/Logout` (POST; GET auto-submits only for same-origin navigations).
- SPA hosting: static files (hashed → immutable, others → no-cache), `.html` only for signed-in users, precompressed
  `.br/.gz`, compression (HTML excluded – BREACH), strict CSP (`SecurityHeadersOptions.SpaContentSecurityPolicy`).
- API-style paths (`ApiPathPrefixes`) answer 401/403, never redirects.
- OIDC mode (`MiddtBffOptions.Oidc`, configuration `Bff:Oidc`, code in `Oidc/`): only when `Authority` is set. The cookie
  stays the default/challenge scheme (API paths keep 401/403); `/Account/Login` GET challenges `BffOidc.SchemeName`
  (code + PKCE, `response_mode=query`, PAR off unless `UsePushedAuthorization`), POST answers 404. `OnTicketReceived`
  (`BffOidc.CreateSessionAsync`) builds the session like `CookieSignInService` (fresh id, tokens + id token in the store,
  `ClaimTypes.*` cookie claims, previous session ended). `OidcTokenService` replaces `ITokenService` (refresh/revoke at
  the provider via the handler's discovery). Logout answers an HTML page with a meta refresh to the end-session endpoint
  (a 302 after the form post would violate CSP `form-action 'self'`). All server-to-server calls use the
  `BffOidc.BackchannelClientName` HttpClient (`BackchannelRewriteHandler`); tests replace its primary handler
  (`tests/…/Bff/BffOidcTests.cs`, `FakeOidcProvider`). Never put tokens into authentication properties (`SaveTokens=false`).
