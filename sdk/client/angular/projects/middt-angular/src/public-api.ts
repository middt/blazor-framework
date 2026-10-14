/*
 * Public API of @middt/angular – the Angular frontend of the Middt framework (counterpart of Middt.Framework.Blazor.Web).
 * Built on @middt/ui-core (contracts, portable expressions, API clients) and Angular Material.
 */

// core: configuration, BFF authentication, notifications
export * from './lib/core/config';
export * from './lib/core/bff-fetch';
export * from './lib/core/auth.service';
export * from './lib/core/guards';
export * from './lib/core/notify.service';
export * from './lib/core/signalr';

// ui: JSON layouts (renderer, widgets, page/view hosts, runtime editor)
export * from './lib/ui/layout-page.component';
export * from './lib/ui/layout-host.component';
export * from './lib/ui/layout-editor.component';
export * from './lib/ui/widget-base';
export * from './lib/ui/widget-registry';
export * from './lib/ui/node.component';
export * from './lib/ui/display-widgets';
export * from './lib/ui/grid-widget.component';
export * from './lib/ui/record-widget.component';
export * from './lib/ui/data-grid.component';
export * from './lib/ui/field-editor.component';
export * from './lib/ui/dialogs';
export * from './lib/ui/lookup-cache.service';
export * from './lib/ui/layout-cache.service';
export * from './lib/ui/render-context';
export * from './lib/ui/layout-json';

// workflow: instance list/detail, wizard, JSON Schema forms, view registry
export * from './lib/workflow/workflow-views';
export * from './lib/workflow/workflow-client';
export * from './lib/workflow/workflow-dialog.component';
export * from './lib/workflow/workflow-view-outlet.component';
export * from './lib/workflow/json-schema-form.component';
export * from './lib/workflow/workflow-instance-list.component';
export * from './lib/workflow/workflow-instance-detail.component';
