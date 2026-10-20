# @middt/ui-core

Framework-agnostic TypeScript core of the **Middt server driven UI**. It lets an Angular (or React, Vue, plain DOM)
frontend render the same JSON layouts as the Blazor renderer of the framework:

| Module | Port of (.NET reference) |
| --- | --- |
| `types.ts` – layout document, nodes, menu, summary, revisions, widget specs, validation result, response envelope | `Middt.Framework.Ui.Abstractions/Model/UiLayoutDocument.cs`, `BaseResponseDataModel.cs` |
| `expression.ts` – the portable expression language (`PortableExpression`) | `Middt.Framework.Expressions/Portable/*` |
| `render-context.ts` – `UiRenderContext` (text, format, isVisible, with, idFor), `UiFormats`, `UiUrl` | `Middt.Framework.Ui.Abstractions/Rendering/UiRenderContext.cs` |
| `fields.ts` – grid columns / form fields, validation (Turkish messages) | `Middt.Framework.Ui.Abstractions/Model/UiFieldModels.cs` |
| `client.ts` – `UiApiClient` (layout API) and `UiDataClient` (CRUD controllers) | `Middt.Framework.Client/Http/UiClients.cs`, `UiEndpoints.cs` |
| `workflow-ui.ts` – `jsonPatchDiff`, `workflowTransitionPrefill` / `workflowTransitionInput` (forms of `input.format: jsonPatch` transitions) | `Middt.Framework.Expressions/JsonPatch.cs`, `Middt.Framework.Blazor/Workflow/WorkflowUi.cs` |

The **Blazor implementation is the reference**. Expressions must behave the same in every renderer, so both
implementations run the shared conformance suite [`spec/expressions.conformance.json`](../../../spec/expressions.conformance.json)
(C#: `PortableExpressionConformanceTests`, here: `test/conformance.test.ts`). To change the language, change the suite first
and make both implementations pass it. The JSON Patch diff works the same way with
[`spec/json-patch.conformance.json`](../../../spec/json-patch.conformance.json) (C#: `JsonPatchConformanceTests`, here:
`test/json-patch.test.ts`).

The sources are plain, erasable TypeScript (no enums, namespaces or parameter properties) with no runtime
dependencies: Node.js ≥ 23.6 runs them directly, and bundlers (Angular CLI, Vite, esbuild) compile them as-is.

```sh
npm test          # node --test test/
```

## How a renderer uses it

1. **Fetch the layout** – `GET api/ui/layouts/{key}` returns a `UiLayoutDocument`; `GET api/ui/layouts?kind=page` feeds the menu.
2. **Render nodes by widget name** – every `UiNode` has `widget`, optional `id`, `visibleWhen`, `children` and its
   properties inline (`uiNodeProps(node)`). Map widget names (`heading`, `text`, `fields`, `alert`, `badge`, `section`,
   `columns`, `divider`, `list`, `link`, `grid`, `record` and any custom ones listed by `GET api/ui/widgets`) to components.
3. **Evaluate with this package** – `ctx.isVisible(node)`, `ctx.text(node.text)`, `ctx.format(value, column.format)`,
   `ctx.with('row', item)` for list/grid rows, `ctx.with('record', loaded)` for `record` widgets, `ctx.render(node.filter)`
   for template maps. Only render links that pass `UiUrl.isSafe` (navigation from row actions: `UiUrl.isLocal`).
4. **Call CRUD through the data client** – `UiDataClient.getItems(entity, UiDataClient.searchRequest(filter, page, size, column.sortProperty, ascending))`,
   `getById`, `insert`, `update`, `delete`; validate forms with `UiField.parseAll(node.form)` + `field.validate(model)`.

```ts
import { UiApiClient, UiDataClient, UiRenderContext, UiColumn } from '@middt/ui-core';

const api = new UiApiClient({ credentials: 'include' });            // same origin, cookie auth through a BFF
const data = new UiDataClient({ credentials: 'include', apiVersion: '1.0' });

const layout = (await api.get('bike-category')).data!;
const ctx = new UiRenderContext({ user: { id: '7', name: 'ayse', roles: ['manager'] }, variables: { query: { id: '42' } } });

for (const node of layout.body) {
  if (!ctx.isVisible(node)) continue;
  if (node.widget === 'heading') console.log(ctx.text(node.text as string));
  if (node.widget === 'grid') {
    const columns = UiColumn.parseAll(node.columns);
    const filter = ctx.render(node.filter ?? {}) as Record<string, unknown>;
    const page = await data.getItems(node.entity as string, UiDataClient.searchRequest(filter, 1, 10, columns[0]?.sortProperty, true));
    for (const row of page.data ?? []) {
      const rowCtx = ctx.with('row', row);
      console.log(columns.map(c => (c.template ? rowCtx.text(c.template, c.format) : rowCtx.format(row[c.field], c.format))));
    }
  }
}
```

In Angular, wrap the clients in injectable services (pass `fetch` if you want to route through `HttpClient`
interceptors, or `headers: () => ({ Authorization: 'Bearer …' })` for token auth) and build one component per widget
that receives the node and its `UiRenderContext`.

## The expression language

Variables of layouts: `data`, `query`, `row`, `record`, `instance`, plus `user` (`Name`, `Id`, `Roles`, `IsInRole(r)`),
`now` (ISO UTC text) and `Math`. Operators `! - * / % + < <= > >= == != && || ?? ?:`; methods `Contains StartsWith EndsWith
ToUpper ToLower Trim`, array `Contains`, `Math.Round(x[, digits]) Abs Floor Ceiling Min Max`. Unknown variables and methods
are syntax errors (`validate`), evaluation never throws (missing members, wrong types, division by zero → `null`).
Templates embed expressions: `"Sipariş #{{ data.amount }}"`; a template that is a single placeholder keeps the value type.

```ts
const engine = new PortableExpression(['data']);
engine.validate('data.amount >');                       // error message
engine.evaluateCondition('data.amount > 5000', { variables: { data: { amount: 15000 } }, user: { roles: [] } }); // true
engine.render({ title: 'Order {{ data.id }}', id: '{{ data.id }}' }, ctx);  // { title: 'Order 42', id: 42 }
```

## Formats

`format` accepts the portable formats only (`UiFormats.isValid`): `N0`–`N6` (grouped), `F0`–`F6` (fixed), dates `d`, `D`,
`g`, `t`; other formats are ignored. Numbers round half away from zero on their shortest decimal text (like .NET
`decimal`), booleans are `Evet`/`Hayır`, the locale defaults to `tr-TR` (`locale` option), dates with a zone are shown
in `timeZone` (default: the browser's), dates without a zone keep their wall clock.

## Known differences from .NET

- Date strings: only ISO 8601 text is recognized (`DateTime.TryParse` accepts more forms).
- Numbers without a format: the original JSON text (`12.50`) is not available after `JSON.parse`, so `12,5` is shown.
- JSON text of objects/arrays is `JSON.stringify` (the C# evaluator matches it; edge cases such as integer-like keys,
  which JavaScript orders first, differ).
- The default user of an `ExpressionContext` is anonymous here; C# defaults to a system user that passes every role check.
