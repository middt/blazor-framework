# Copilot instructions

Follow [`AGENTS.md`](../AGENTS.md) (repository map, commands, conventions, definition of done) and the nested
`AGENTS.md` of the folder you edit. Task playbooks: [`docs/ai/`](../docs/ai/README.md).

Key rules:
- .NET 10, warnings are errors in CI (`dotnet build Middt.Framework.slnx -p:ContinuousIntegrationBuild=true`), central
  package versions, file-scoped namespaces, primary constructors, async + CancellationToken, InvariantCulture.
- Layering: `sdk/shared` has no ASP.NET/UI deps; `sdk/server` never references client/frontend packages.
- Contracts (layout model, widget specs, portable expressions, DTOs) change in .NET **and** `sdk/client/typescript/ui-core`
  together, with `spec/` updated.
- Blazor and Angular must stay feature-equal with identical markup/classes/ids and Turkish UI texts.
- `ResultEnum.Success = 0`. Tokens never reach the browser (BFF). Don't weaken security defaults.
