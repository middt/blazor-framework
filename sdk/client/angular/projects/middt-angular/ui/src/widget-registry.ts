import { EnvironmentProviders, inject, InjectionToken, makeEnvironmentProviders, Type } from '@angular/core';
import { UiWidgetNames } from '@middt/ui-core';
import {
  UiAlertWidget,
  UiBadgeWidget,
  UiColumnsWidget,
  UiDividerWidget,
  UiFieldsWidget,
  UiHeadingWidget,
  UiLinkWidget,
  UiListWidget,
  UiSectionWidget,
  UiTextWidget,
} from './display-widgets';
import { UiGridWidget } from './grid-widget.component';
import { UiLayoutWidget } from './layout-widget.component';
import { UiRecordWidget } from './record-widget.component';

/**
 * A widget of the Angular renderer (counterpart of a UiWidgetSpec + component pair of UiBlazorWidgets). The component
 * should extend {@link UiWidgetBase} (inputs `node` and `context`). A definition without component declares a widget
 * the server knows but this application does not render (an error box is shown).
 */
export interface UiWidgetDefinition {
  name: string;
  component?: Type<unknown> | null;
  /** Interactive widgets (data access, forms) render a placeholder in a non-interactive context. */
  interactive?: boolean;
  /** Short description (placeholder text of interactive widgets). */
  description?: string;
}

/** Built-in widgets: display components and the interactive `grid`, `record` and `layout`. */
export function defaultUiWidgets(): UiWidgetDefinition[] {
  return [
    { name: UiWidgetNames.heading, component: UiHeadingWidget, description: 'Title text.' },
    { name: UiWidgetNames.text, component: UiTextWidget, description: 'Paragraph.' },
    { name: UiWidgetNames.fields, component: UiFieldsWidget, description: 'Label/value list.' },
    { name: UiWidgetNames.alert, component: UiAlertWidget, description: 'Highlighted message.' },
    { name: UiWidgetNames.badge, component: UiBadgeWidget, description: 'Small status label.' },
    { name: UiWidgetNames.section, component: UiSectionWidget, description: 'Card with an optional title.' },
    { name: UiWidgetNames.columns, component: UiColumnsWidget, description: 'Shows its children side by side (one column each, stacked on small screens).' },
    { name: UiWidgetNames.divider, component: UiDividerWidget, description: 'Horizontal line.' },
    { name: UiWidgetNames.list, component: UiListWidget, description: "Repeats its children for each item of an array (the item is 'row')." },
    { name: UiWidgetNames.link, component: UiLinkWidget, description: 'Link to a local page (or http/https URL).' },
    {
      name: UiWidgetNames.grid,
      component: UiGridWidget,
      interactive: true,
      description: 'Server paged data grid of an entity with search, row actions and optional create/edit/delete.',
    },
    { name: UiWidgetNames.record, component: UiRecordWidget, interactive: true, description: "Loads one entity by id and shows its children with it as 'record'." },
    { name: UiWidgetNames.layout, component: UiLayoutWidget, interactive: true, description: "Embeds a reusable view layout by key (optionally scoping 'data' to a sub-expression)." },
  ];
}

/** Custom widget registrations (multi provider; see {@link provideMiddtUiWidgets}). */
export const MIDDT_UI_WIDGET_REGISTRATIONS = new InjectionToken<readonly UiWidgetDefinition[][]>('MIDDT_UI_WIDGET_REGISTRATIONS');

/** Widget name → definition: the built-ins plus every registration (a registration replaces a built-in of the same name). */
export const MIDDT_UI_WIDGETS = new InjectionToken<ReadonlyMap<string, UiWidgetDefinition>>('MIDDT_UI_WIDGETS', {
  providedIn: 'root',
  factory: () => {
    const widgets = new Map<string, UiWidgetDefinition>();
    for (const widget of defaultUiWidgets()) {
      widgets.set(widget.name, widget);
    }

    for (const registration of inject(MIDDT_UI_WIDGET_REGISTRATIONS, { optional: true }) ?? []) {
      for (const widget of registration) {
        widgets.set(widget.name, widget);
      }
    }

    return widgets;
  },
});

/**
 * Registers custom widgets (also add their specs on the API with `MiddtUiOptions.AddWidget`, so layouts using them validate):
 * <code>
 * providers: [provideMiddt(), provideMiddtUiWidgets({ name: 'chart', component: ChartWidget, description: 'Chart.' })]
 * </code>
 */
export function provideMiddtUiWidgets(...widgets: UiWidgetDefinition[]): EnvironmentProviders {
  return makeEnvironmentProviders([{ provide: MIDDT_UI_WIDGET_REGISTRATIONS, useValue: widgets, multi: true }]);
}
