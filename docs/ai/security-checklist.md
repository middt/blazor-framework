# Security checklist for changes

Answer these before finishing a change (add tests for anything security relevant):

- **AuthN/AuthZ**: is every new endpoint authenticated? Role checks? Record level (`AuthorizeAsync`) / row level
  (`ApplyAccessFilter`) / workflow `instanceAccess` where data belongs to users?
- **Input**: request bodies validated and size-bounded? Page sizes clamped? No dynamic SQL/LINQ strings with user input
  (values as parameters)? Regex with timeouts?
- **Output**: data HTML-encoded (Razor/Angular do it – never `MarkupString`/`bypassSecurityTrust*`/`Raw()` with data)?
  URLs through `UiUrl`/`LocalUrl`? Error messages without internal details (ProblemDetails, hidden workflow errors)?
- **Tokens/session**: tokens never reach the browser (BFF), no new proxy routes around `X-CSRF`/deny list, cookies
  HttpOnly/SameSite, re-login revokes the old session.
- **Outbound calls**: allow-listed hosts, no redirects, timeouts (`http` task, HttpClients).
- **Secrets/config**: nothing in source or compose defaults; `.env` / user secrets / environment variables only.
- **Headers**: CSP not loosened (no inline scripts), security headers kept, compression not applied to pages that
  reflect input next to secrets (BREACH).
- **Dependencies**: `dotnet list package --vulnerable --include-transitive`, `npm audit --omit=dev` clean.
