# Playbook: 100% Backend-Driven JSON UI, Workflows & Reusable Layout Widgets

Every screen, navigation menu item, workflow list table, transition form, state detail view, and reusable sub-widget in Middt Framework is defined **entirely in backend JSON** (`Workflows/*.json` + `Layouts/*.json`) with **zero custom `.razor` or `.ts` view code**.

Both **Blazor Server** and **Angular** render these JSON definitions identically at runtime.

---

## 1. Architecture Overview

| UI Surface | JSON Source | Rendered By (Blazor / Angular) |
|---|---|---|
| **Left Navigation Menu** (Workflows) | `Workflows/*.json` (`key`, `title`, `icon`) via `GET /api/workflows` | `MenuService` / `MenuService` |
| **Left Navigation Menu** (UI Pages) | `Layouts/*.json` (`kind: "page"`, `key`, `title`, `menu`) via `GET /api/ui/layouts` | `MenuService` / `MenuService` |
| **Workflow Instance List Table** (`/workflow/{key}`) | `Workflows/*.json` (`dataSchema.properties`, `x-list`, `x-mask`) | `WorkflowInstanceList` / `MiddtWorkflowListComponent` |
| **Workflow Start & Action Forms** | `Workflows/*.json` (`transitions[].input.schema`, `x-lookup`, `x-mask`) | `JsonSchemaForm` / `MiddtSchemaFormComponent` |
| **Workflow State Detail Screen** (`/workflow/{key}/{id}`) | `Workflows/*.json` (`states[].view.layout`) $\rightarrow$ `Layouts/*.json` (`kind: "view"`) | `WorkflowInstanceDetail` + `UiLayoutHost` / `MiddtWorkflowDetailComponent` + `MiddtLayoutHostComponent` |
| **Reusable Nested Widgets** | `Layouts/*.json` (`kind: "view"`) embedded via `{ "widget": "layout", "key": "...", "data": "..." }` | `UiLayoutWidget` / `UiLayoutWidget` |
| **Directory / CRUD / Overview Pages** (`/ui/{key}`) | `Layouts/*.json` (`kind: "page"`) | `UiPageHost` / `MiddtLayoutPageComponent` |

> **Rule**: Never create hardcoded `.razor` or Angular `.ts` view components for business workflows or screens unless you are building a brand-new SDK primitive widget (`sdk/client/...`). All domain screens belong in `Layouts/*.json` and `Workflows/*.json`.

---

## 2. Dynamic Menu & List Columns from Workflow JSON (`Workflows/*.json`)

A workflow definition automatically registers:
1. Its **sidebar menu item** (`title` + `icon`, navigating to `/workflow/{key}`).
2. Its **instance list columns** (`dataSchema.properties` in property order).
3. Its **dynamic REST query endpoint** (`POST /api/v1/{entity}/getitems`, `/getall`, `/getbyid`), resolved dynamically by `WorkflowInstanceDataQuery` (accepting either the workflow `key` like `customer-onboarding`, its PascalCase form `CustomerOnboarding`, or its first segment `Customer`).

```json
{
  "key": "customer-onboarding",
  "version": 1,
  "title": "Müşteri Kaydı & KYC",
  "icon": "badge",
  "description": "Gerçek ve tüzel kişi müşteri kabul, kimlik doğrulama ve KYC uyumluluk akışı",
  "dataSchema": {
    "type": "object",
    "required": ["fullName", "identityNumber", "segment"],
    "properties": {
      "fullName":       { "type": "string", "title": "Ad Soyad / Ünvan", "x-search": true },
      "identityNumber": { "type": "string", "title": "TCKN / VKN", "x-mask": "middle" },
      "segment":        { "type": "string", "title": "Segment", "enum": ["Bireysel", "KOBİ", "Kurumsal"], "x-search": true },
      "kycScore":       { "type": "integer", "title": "KYC Skoru" },
      "riskLevel":      { "type": "string", "title": "Risk Seviyesi", "enum": ["Düşük", "Orta", "Yüksek"], "x-search": true },
      "approvalNote":   { "type": "string", "title": "Uyum Onay Notu", "x-list": false }
    }
  }
}
```

### `dataSchema` Extensions
- **`"x-list": false`**: Hides the property from the workflow instance table (`/workflow/{key}`) while keeping it in the schema for documentation, validation, and detail layouts.
- **`"x-search": true`**: Adds a filter control to the workflow instance list toolbar (`WorkflowInstanceList` in Blazor & Angular). Properties with `"enum": [...]` render as a selectable dropdown (`AllowClear`); other scalar properties render as a text search box.
- **`"x-mask": "middle" | "end"`**: Masks sensitive values when returned by `WorkflowInstanceDataQuery`.
- **`"x-roles": ["manager"]`**: Restricts field visibility to specific roles.
- **`"x-lookup"`**: Populates dropdown options dynamically from an entity endpoint in transition forms:
  ```json
  "customerKey": {
    "type": "string",
    "title": "Müşteri",
    "x-lookup": {
      "entity": "CustomerOnboarding",
      "valueField": "key",
      "textField": "fullName"
    }
  }
  ```

---

## 3. Backend-Driven State Views (`view.layout`)

Every `Human` or `Finish` state in a workflow points to a `kind: "view"` layout key:

```json
{
  "key": "kyc-review",
  "title": "Uyum / KYC İncelemesi",
  "subType": "Human",
  "view": { "layout": "kyc-review" },
  "transitions": [ ... ]
}
```

When a user opens `/workflow/customer-onboarding/{instanceId}`, `WorkflowInstanceDetail` (Blazor) or `MiddtWorkflowDetailComponent` (Angular) fetches the `kyc-review` layout from `/api/ui/layouts/kyc-review` and passes `{ data: instance.data, instance: ... }` into the layout's `UiRenderContext`.

Inside the `kind: "view"` layout:
- `{{ data.fullName }}` and `{{ instance.key }}` render properties from the workflow instance.
- `visibleWhen` expressions (`instance.state == "kyc-review"`, `data.kycScore >= 750`, `user.IsInRole("manager")`) dynamically show or hide alerts, badges, fields, or sections.

---

## 4. Reusable Widgets via `"widget": "layout"` (Composition & Nesting)

You can build reusable partial layouts (`kind: "view"`) and embed them inside any other layout (`page` or `view`) using `"widget": "layout"`.

### Spec of `"widget": "layout"` (`UiWidgets.Layout`)
| Property | Type | Required | Description |
|---|---|---|---|
| `widget` | `"layout"` | Yes | Identifies the embedded layout widget |
| `key` | `string` (`a-z 0-9 - _ .`) | Yes | Key of the target `UiLayoutDocument` stored in the backend |
| `data` | `string` (expression) | No | Optional portable expression evaluated against the parent scope to bind as the child layout's `data` variable (e.g. `"data.applicant"` or `"row"`). When omitted, the child layout inherits the parent's `UiRenderContext` (`data`, `instance`, `record`, `row`, `query`, `user`). |
| `title` | `string` (template) | No | Optional heading (`{{ }}` template) rendered above the embedded layout |
| `visibleWhen` | `string` (expression) | No | Optional condition controlling whether the embedded layout is rendered |

> **Cycle & Depth Guards**:
> 1. **Compile/Seed-time Validation**: `UiLayoutValidator.Validate` rejects direct self-embedding (`layout 'x' cannot embed itself`), and `UiLayoutValidator.ValidateGraph` (run by `UiStartup` at API startup and `UiSampleLayoutTests`) rejects missing target layouts, non-`view` targets, and multi-hop circular chains (`A -> B -> A`).
> 2. **Runtime Recursion Guard**: Both Blazor (`UiLayoutWidget`) and Angular (`UiLayoutWidget`) enforce a maximum nesting depth of **5** (`MaxDepth = 5`).

### Pattern A: Shared Footer / Banner Across Multiple Workflows (Inheriting Parent Scope)

1. Define the reusable partial layout `Layouts/workflow-footer.json`:
   ```json
   {
     "$schema": "../../../../spec/ui-layout.schema.json",
     "key": "workflow-footer",
     "title": "Ortak Akış Durum Altbilgisi",
     "kind": "view",
     "version": 1,
     "body": [
       { "widget": "divider" },
       { "widget": "text", "style": "small", "text": "Durum: {{ instance.stateTitle }} · Akış: {{ instance.workflow }}" }
     ]
   }
   ```

2. Embed it inside `kyc-review.json`, `credit-review.json`, `order-review.json`, `order-result.json`, and `risk-detail.json`:
   ```json
   { "widget": "layout", "key": "workflow-footer" }
   ```

### Pattern B: Reusable Widget Groups Scoped with `"data"` (`customer-summary-card` & `risk-eval-card`)

1. **`Layouts/customer-summary-card.json`** groups a `section`, conditional `badge`s, and `fields` (`fullName`, `identityNumber`, `segment`, `riskLevel`, `monthlyIncome`, `email`, `phone`):
   - In **`kyc-review.json`** (`customer-onboarding`), where the root `data` IS the customer object, it is embedded without `data`:
     ```json
     { "widget": "layout", "key": "customer-summary-card" }
     ```
   - In **`credit-review.json`** (`credit-allocation`), where the `loadWorkflow` task loaded the customer into `data.customer`, the **exact same widget group** is embedded with `"data": "data.customer"`:
     ```json
     {
       "widget": "layout",
       "key": "customer-summary-card",
       "data": "data.customer",
       "visibleWhen": "data.customer != null"
     }
     ```

2. **`Layouts/risk-eval-card.json`** groups the risk score `badge`s and evaluation `fields` (`checkType`, `riskScore`, `recommendedLimit`, `underwriterNote`, `evaluatedAt`):
   - In **`risk-detail.json`** (`risk-limit-eval`), it is embedded at root scope:
     ```json
     { "widget": "layout", "key": "risk-eval-card" }
     ```
   - In **`credit-review.json`** (`credit-allocation`), where the `risk-limit-eval` subflow wrote its result into `data.riskResult`, the **exact same widget group** is embedded with `"data": "data.riskResult"`:
     ```json
     {
       "widget": "layout",
       "key": "risk-eval-card",
       "data": "data.riskResult",
       "visibleWhen": "data.riskResult != null"
     }
     ```

---

## 5. Complete Reference of Built-in JSON Widgets (`UiWidgets.All`)

| Widget | Category | Key Properties | Description |
|---|---|---|---|
| `section` | Container | `title`, `subtitle`, `children` | Titled card/panel grouping child widgets |
| `columns` | Container | `widths: ["2", "1"]`, `children` | Responsive multi-column layout |
| `list` | Container | `source` (array expression), `emptyText`, `children` | Repeats `children` for each item in `source` as `row` |
| `layout` | Container / Reusable | `key`, `data` (optional expression), `title` | Embeds another JSON layout by `key` with optional `data` scope |
| `heading` | Display | `text`, `level` (`1`..`4`) | Section or page heading (`{{ }}` template) |
| `text` | Display | `text`, `style` (`body`/`lead`/`muted`/`small`/`code`) | Paragraph or inline text (`{{ }}` template) |
| `alert` | Display | `severity` (`info`/`success`/`warning`/`danger`), `title`, `text` | Callout banner (`{{ }}` template) |
| `badge` | Display | `text`, `color` (`neutral`/`info`/`success`/`warning`/`danger`) | Colored status pill |
| `divider` | Display | – | Horizontal separator line |
| `link` | Display | `text`, `href` (`{{ }}` template, validated by `UiUrl.IsSafe`), `style` (`link`/`button`) | Safe navigation link or action button |
| `fields` | Display | `items: [{ label, value, format, visibleWhen }]` | Label/value definition list (`format`: `N0`–`N6`, `F0`–`F6`, `d`, `D`, `g`, `t`) |
| `record` | Interactive | `entity`, `recordId` (`{{ }}` template), `emptyText`, `children` | Loads one entity by ID and renders `children` with `record` in scope |
| `grid` | Interactive | `entity`, `pageSize`, `filter`, `columns`, `search`, `create`, `edit`, `delete`, `form`, `rowActions` | Full server-paged CRUD / directory data grid |

---

## 6. Verification Checklist

Whenever you add or modify a JSON layout or workflow seed in `demo/Middt.Demo.Api/`:

1. Run schema & sample validation tests:
   ```bash
   dotnet test --project tests/Middt.Framework.Tests -- --filter-class "*UiSampleLayoutTests" --filter-class "*WorkflowDefinitionTests"
   ```
2. Remember that `LayoutSeedHostedService` **only overwrites existing DB records when `version` is incremented** (`existing.Version < incoming.Version`). Bump `"version"` in your JSON file when updating an existing layout so running environments pick up the change automatically on restart.
