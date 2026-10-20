# Security checklist for changes

Answer these before finishing a change (add tests for anything security relevant):

- **AuthN/AuthZ**: is every new endpoint authenticated? Role checks? Record level (`AuthorizeAsync`) / row level
  (`ApplyAccessFilter`) / workflow `instanceAccess` where data belongs to users? For workflow lists, are `roles` and
  `instanceAccess` checked against each instance's stored definition version before counting and paging?
- **Input**: request bodies validated and size-bounded? Page sizes clamped? No dynamic SQL/LINQ strings with user input
  (values as parameters)? Regex with timeouts?
- **Output**: data HTML-encoded (Razor/Angular do it – never `MarkupString`/`bypassSecurityTrust*`/`Raw()` with data)?
  URLs through `UiUrl`/`LocalUrl`? Error messages without internal details (ProblemDetails, hidden workflow errors)?
- **Tokens/session**: tokens never reach the browser (BFF), no new proxy routes around `X-CSRF`/deny list, cookies
  HttpOnly/SameSite, re-login revokes the old session.
- **Outbound calls**: allow-listed hosts, no redirects, timeouts (`http` task, HttpClients).
- **Workflow retries**: are secret input fields declared in `input.sensitive` and absent from pending input/history?
- **Workflow messages**: do event transitions that change money or status carry `roles`? The sender is checked at send time and buffered messages later run as the system; message bodies are validated by the transition `input.schema` – keep `additionalProperties: false`.
- **Background tasks**: an `http` background task still honours `AllowedHttpHosts`; its side effects can be retried after a crash, so the receiver must deduplicate on `Idempotency-Key` (stable per background job).
  Can external task side effects and event handlers tolerate retries or duplicate delivery? Is the HTTP task's
  `Idempotency-Key` honored by the receiver when needed?
- **Secrets/config**: nothing in source or compose defaults; `.env` / user secrets / environment variables only.
- **Headers**: CSP not loosened (no inline scripts), security headers kept, compression not applied to pages that
  reflect input next to secrets (BREACH).
- **Dependencies**: `dotnet list package --vulnerable --include-transitive`, `npm audit --omit=dev` clean.
