# sdk/client/bff – backend-for-frontend (read the root AGENTS.md first)

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
