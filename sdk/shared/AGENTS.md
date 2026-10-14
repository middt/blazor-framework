# sdk/shared – contracts (read the root AGENTS.md first)

Model · Expressions · Ui.Abstractions. Used by **every** layer and mirrored in TypeScript (`sdk/client/typescript/ui-core`).

- **No ASP.NET Core, Razor, Radzen, EF Core** references here (LayeringTests fail). Plain `Microsoft.NET.Sdk` + BCL.
- `Middt.Framework.Model`: wire DTOs only (no logic, no dependencies). Namespaces are historic
  (`Middt.Framework.Common.Model.Data`, `Middt.Framework.Api.Configuration.Model`, `Middt.Framework.Model.Workflow`, …).
  Changing a DTO changes the wire format → update `sdk/client/typescript/ui-core/src/types.ts` / `workflow.ts` too.
- `Middt.Framework.Expressions`:
  - `Portable/` = the **portable expression language** used by layouts. Semantics are specified by
    `spec/expressions.conformance.json` and implemented identically in `ui-core/src/expression.ts`. Any semantic change:
    add conformance cases first, then change C# **and** TS (playbook: `docs/ai/change-expression-language.md`).
    Evaluation never throws (missing member / bad operands / div by 0 → null); only syntax errors throw
    `PortableExpressionException`. Unknown variables/methods are rejected at parse time – keep the whitelist small.
  - `ExpressionEvaluator` (DynamicExpresso) = richer C# dialect for **server-only** workflow rules/mappings. Never expose
    it to layouts/clients; reflection and type access stay disabled.
- `Middt.Framework.Ui.Abstractions`: layout model (`UiLayoutDocument`, `UiNode` + its JSON converter), widget specs
  (`UiWidgets`, `UiWidgetCatalog`), `UiLayoutValidator`, `UiRenderContext`/`UiFormats`/`UiUrl`, field models,
  `UiLayoutSchema` (JSON Schema generator). Adding/changing a widget spec → regenerate `spec/ui-layout.schema.json`
  (`MIDDT_UPDATE_SPEC=1 dotnet test …`) and implement it in Blazor, Angular and `UiHtmlRenderer`
  (playbook: `docs/ai/add-layout-widget.md`).
- `UiUrl` and `LocalUrl` are security boundaries (open redirect / off-site navigation): keep rejecting `//`, `/\`, control
  characters and leading/trailing whitespace; keep the TS twin identical.
