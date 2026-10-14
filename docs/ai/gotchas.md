# Gotchas (learned the hard way)

| Symptom | Cause | Fix / rule |
|---|---|---|
| Stored layouts contain invalid JSON | `[JsonExtensionData] JsonObject` serialises nested props wrongly on .NET 10 | `UiNode` uses `UiNodeJsonConverter`; don't reintroduce extension data |
| Expression returns `"\"text\""` / numbers compare false | switch expression typed as `JsonNode` converts primitives back via implicit operators | cast switch arms to `object` |
| Random test failures reading JSON | `JsonObject` materialises lazily, not thread safe | parse per test/thread |
| Number text differs between .NET and TS | .NET "R" switches to exponent at 1E+15/1E-05 | portable rule = ECMAScript `Number.toString`, implemented explicitly in C# |
| Blazor and Angular demos log each other out | cookies ignore ports | unique `MiddtBffOptions.CookieName` per app |
| Menu never shows layouts | `result === 1` check | `ResultEnum.Success = 0` – use `isSuccess()` |
| Workflow tables 95 % fragmented | random-order GUID keys in SQL Server | engine's sequential id generator |
| Each search value creates a SQL plan | Dynamic LINQ inlines constants | expression trees with captured values (parameters) |
| Angular app has two Angular instances / DI errors | linked `file:` packages resolve their own node_modules | `preserveSymlinks: true` in the app |
| Angular page doesn't update after `await` | zoneless + OnPush | update signals, not plain fields |
| Blazor circuit crashes after closing a dialog | cancelled task cached in a shared cache | cache-owned tokens, evict canceled/faulted |
| Login retried 3× and took 6.7 s | resilience handler retried POST/429 | retries only for safe methods, never 429 |
| `docker compose up` fails with "Set JWT_SECRET" | secrets have no defaults | `cp .env.example .env` and fill it |
| E2E logout test hangs | typed `/Account/Logout` needs confirmation (logout CSRF protection) | navigate from the app (same-origin) or click the button |
