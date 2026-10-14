---
description: Add a layout widget to the contract and every renderer (static HTML, Blazor, Angular)
argument-hint: <widget-name> <what it shows/does>
---

Add the layout widget: $ARGUMENTS

Follow `docs/ai/add-layout-widget.md`: spec in `UiWidgets`, regenerate `spec/ui-layout.schema.json`, `UiHtmlRenderer`,
Blazor component + `UiBlazorWidgets` registration, Angular component + `widget-registry.ts`, identical markup/classes,
tests (validator, schema, static + Blazor rendering parity), README widget table. Then run `/verify`.
