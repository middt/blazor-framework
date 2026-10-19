# tests – .NET tests (read the root AGENTS.md first)

One project: `Middt.Framework.Tests` (xunit.v3 + AwesomeAssertions on Microsoft.Testing.Platform).

```bash
dotnet test --project tests/Middt.Framework.Tests
dotnet test --project tests/Middt.Framework.Tests -- --filter-class "*SpaBffHardeningTests"
```

- Folders mirror the code: Api, Workflow, Ui, Bff, Blazor, Client, Security, Messaging, Expressions,
  Architecture (layering), Infrastructure.
- Use the existing hosts instead of inventing new ones: `TestApiHost`, `WorkflowTestHost`, `UiTestHost`,
  `SpaBffTestHost`, `BlazorTestHost`; SQLite in-memory (keep the connection open), `FakeTimeProvider`,
  `TestContext.Current.CancellationToken`.
- Tests run in parallel: no shared mutable statics; parse `JsonObject` per test (lazy materialisation isn't thread-safe).
- Warnings are errors in CI – analyzers apply to tests too (`CultureInfo.InvariantCulture`, `ConfigureAwait` not needed).
- Spec files: `Expressions/PortableExpressionConformanceTests` (spec/expressions.conformance.json) and
  `Ui/UiLayoutSchemaTests.Schema_file_is_up_to_date` (regenerate with `MIDDT_UPDATE_SPEC=1`).
- Architecture: `Architecture/LayeringTests` – if it fails, fix the dependency, not the test.
