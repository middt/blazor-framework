# sdk/client/Middt.Framework.Blazor – Blazor frontend (read the root AGENTS.md first)

`Middt.Framework.Blazor` (Blazor Server + Radzen).

- Authentication comes from `Middt.Framework.Bff`; `ServerSessionState` reads tokens from the server store for the
  circuit, API calls go through `Middt.Framework.Client` typed clients.
- Layout renderer lives in `Middt.Framework.Blazor/Ui/` (namespace `Middt.Framework.Blazor.Ui`): `UiNodeView`
  display widgets, `UiGridWidget` (`grid`), `UiRecordWidget` (`record`), `UiLayoutWidget` (`layout` – embeds another JSON
  layout by `key` with optional `data` scope and `MaxDepth = 5` recursion guard), `UiFieldEditor`, `UiLayoutHost`,
  `UiPageHost`, `UiLayoutView`, `UiLayoutEditor`, registry `UiBlazorWidgets`. Markup must match `UiHtmlRenderer` and the Angular widgets (same
  classes, ids, aria labels) – a parity test compares visible text.
- Widgets derive from `UiWidgetBase`, which skips updates with unchanged `Node`/`Context`; override
  `SkipUnchangedParameters` if a widget renders outside state. Pass stable `Variables` dictionaries to `UiLayoutHost`.
- Performance rules: no per-render allocations of parameter dictionaries/contexts, no JS interop on every render,
  cancellation-safe caches (`UiLookupCache`), `PersistentComponentState` for prerender → circuit hand-over,
  fingerprinted assets (`~/` URLs + `WithStaticAssets`).
- Workflow screens: `Workflow/` (list, detail with stepper/wizard, `WorkflowDiagram` SVG flow & execution path diagrams, JSON Schema form, dialogs).
- Tests: `tests/…/Blazor` and `tests/…/Ui/UiBlazorRenderingTests.cs` (HtmlRenderer with `UiBlazorWidgets` registered).
