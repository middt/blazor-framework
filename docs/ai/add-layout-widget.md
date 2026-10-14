# Playbook: add a layout widget

A widget is a **contract** (spec) plus one implementation per renderer. All of them must ship together.

| Step | Where |
|---|---|
| 1. Spec (`UiWidgetSpec`: name, description, props with `UiPropType`, children rule, `Interactive`, extra `Validate`) | `sdk/shared/Middt.Framework.Ui.Abstractions/Widgets/UiWidgets.cs` (built-in) or the app (custom: `MiddtUiOptions.AddWidget(spec, html)` on the API + registrations in each frontend) |
| 2. Validator / schema | validator is spec-driven; regenerate `spec/ui-layout.schema.json` with `MIDDT_UPDATE_SPEC=1 dotnet test --project tests/Middt.Framework.Tests` |
| 3. Static HTML | `sdk/server/Middt.Framework.Ui.Server/Rendering/UiHtmlRenderer.cs` (`html.Text()` for all data – never `Raw()` with data). Interactive widgets render a placeholder automatically. |
| 4. Blazor | component deriving `UiWidgetBase` in `sdk/client/blazor/Middt.Framework.Blazor.Web/Ui/Components/Display` (or `Ui/Widgets`), register in `UiBlazorWidgets.CreateDefault()` |
| 5. Angular | standalone OnPush component extending `UiWidgetBase` in `sdk/client/angular/projects/middt-angular/src/lib/ui`, register in `widget-registry.ts` |
| 6. TypeScript contract | `sdk/client/typescript/ui-core` only if new contract logic (formats, URL rules, field types) is needed |
| 7. Tests | `tests/…/Ui` (validator, schema, `UiHtmlRenderer`, Blazor rendering + **parity** test), ui-core tests if contract logic changed |
| 8. Docs | README widget table |

Rules:
- Same markup and CSS classes in all three renderers (`middt-ui-*` in `sdk/client/typescript/ui-core/styles/middt-ui.css`
  and its copy `sdk/client/blazor/Middt.Framework.Blazor.Web/wwwroot/middt-ui.css` – keep them identical).
- Text properties are templates (`UiPropType.Template`); conditions are `Expression`; formats use `UiPropType.Format`.
- Links/navigation only through `UiUrl.IsSafe/IsLocal`.
