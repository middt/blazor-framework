# Playbook: add a layout widget

> **Tip**: Before adding a new compiled C#/TypeScript widget, check if your component can be built by composing existing widgets (`columns`, `section`, `card`, `stat`, `field`, `badge`, `banner`, `table`, `record`, `list`) inside a `kind: "view"` JSON layout and embedding it via `{ "widget": "layout", "key": "<layout-key>", "data": "<optional-expr>" }`. See [`backend-driven-json-ui.md`](backend-driven-json-ui.md).

When you need a new **primitive** widget, it is a **contract** (spec) plus one implementation per renderer. All of them must ship together:

| Step | Where |
|---|---|
| 1. Spec (`UiWidgetSpec`: name, description, props with `UiPropType`, children rule, `Interactive`, extra `Validate`) | `sdk/shared/Middt.Framework.Ui.Abstractions/Widgets/UiWidgets.cs` (built-in, add to `UiWidgets.All`) or the app (custom: `MiddtUiOptions.AddWidget(spec, html)` on the API + registrations in each frontend) |
| 2. Validator / schema | Validator is spec-driven; regenerate `spec/ui-layout.schema.json` with `MIDDT_UPDATE_SPEC=1 dotnet test --project tests/Middt.Framework.Tests` |
| 3. Static HTML | `sdk/server/Middt.Framework.Ui.Server/Rendering/UiHtmlRenderer.cs` (`html.Text()` for all data – never `Raw()` with data). Interactive widgets (`Interactive = true`, such as `grid`, `record`, `layout`) render a placeholder automatically. |
| 4. Blazor | Component in `sdk/client/blazor/Middt.Framework.Blazor.Web/Ui/Widgets/` (or handled inside `UiNodeView.razor`), register in `UiBlazorWidgets.CreateDefault()` |
| 5. Angular | Standalone OnPush component in `sdk/client/angular/projects/middt-angular/ui/src/`, register in `widget-registry.ts` and export in `public-api.ts` |
| 6. TypeScript contract | Add widget name to `UiWidgetNames` in `sdk/client/typescript/ui-core/src/types.ts` (and any new contract helpers/validators in `sdk/client/typescript/ui-core`) |
| 7. Tests | `tests/Middt.Framework.Tests/Ui` (`UiLayoutSchemaTests`, `UiSampleLayoutTests`, `UiHtmlRenderer`, Blazor rendering + **parity** test), `ui-core` tests if contract logic changed |
| 8. Docs | `README.md` widget table and `docs/ai/backend-driven-json-ui.md` |

Rules:
- Same markup and CSS classes in all three renderers (`middt-ui-*`).
- Text properties are templates (`UiPropType.Template`); conditions are `Expression`; formats use `UiPropType.Format`.
- Links/navigation only through `UiUrl.IsSafe` / `UiUrl.IsLocal`.
