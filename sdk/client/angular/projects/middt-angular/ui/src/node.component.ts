import { NgComponentOutlet } from '@angular/common';
import { ChangeDetectionStrategy, Component, computed, inject, input } from '@angular/core';
import type { UiLayoutDocument, UiNode, UiRenderContext } from '@middt/ui-core';
import { UiProps } from './widget-base';
import { MIDDT_UI_WIDGETS } from './widget-registry';

/** Renders one node: visibility, lookup of the widget component, placeholder for interactive widgets in static rendering. */
@Component({
  selector: 'middt-ui-node',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [NgComponentOutlet],
  host: { style: 'display: contents' },
  template: `
    @switch (state().kind) {
      @case ('unknown') {
        <div class="middt-ui-error" role="alert">Bilinmeyen bileşen: {{ node().widget }}</div>
      }
      @case ('placeholder') {
        <div class="middt-ui-placeholder">{{ state().text }}</div>
      }
      @case ('missing') {
        <div class="middt-ui-error" role="alert">'{{ node().widget }}' bileşeni bu uygulamada kayıtlı değil.</div>
      }
      @case ('component') {
        <ng-container *ngComponentOutlet="state().component!; inputs: inputs()" />
      }
    }
  `,
})
export class UiNodeComponent {
  readonly #widgets = inject(MIDDT_UI_WIDGETS);

  readonly node = input.required<UiNode>();
  readonly context = input.required<UiRenderContext>();

  protected readonly inputs = computed(() => ({ node: this.node(), context: this.context() }));

  protected readonly state = computed(() => {
    const node = this.node();
    const context = this.context();
    if (!context.isVisible(node)) {
      return { kind: 'hidden' } as const;
    }

    const widget = this.#widgets.get(node.widget);
    if (widget === undefined) {
      return { kind: 'unknown' } as const;
    }

    if (widget.interactive && !context.interactive) {
      const title = UiProps.string(node, 'title');
      return { kind: 'placeholder', text: title === null ? (widget.description ?? node.widget) : context.text(title) } as const;
    }

    if (widget.component === null || widget.component === undefined) {
      return { kind: 'missing' } as const;
    }

    return { kind: 'component', component: widget.component } as const;
  });
}

/** Renders a list of nodes with one context. */
@Component({
  selector: 'middt-ui-node-list',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [UiNodeComponent],
  host: { style: 'display: contents' },
  template: `
    @for (node of nodes() ?? []; track $index) {
      <middt-ui-node [node]="node" [context]="context()" />
    }
  `,
})
export class UiNodeListComponent {
  readonly nodes = input<readonly UiNode[] | null | undefined>([]);
  readonly context = input.required<UiRenderContext>();
}

/** Renders a layout document (use the page/layout hosts for loading; this component only renders). */
@Component({
  selector: 'middt-ui-layout-view',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [UiNodeListComponent],
  host: { style: 'display: contents' },
  template: `
    <div class="middt-ui" [attr.data-layout]="layout().key">
      <middt-ui-node-list [nodes]="layout().body" [context]="context()" />
    </div>
  `,
})
export class UiLayoutViewComponent {
  readonly layout = input.required<UiLayoutDocument>();
  readonly context = input.required<UiRenderContext>();
}
