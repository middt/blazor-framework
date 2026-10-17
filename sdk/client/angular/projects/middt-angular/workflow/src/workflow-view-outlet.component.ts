import { Binding, ChangeDetectionStrategy, Component, effect, input, inputBinding, output, outputBinding, Type, untracked, ViewContainerRef } from '@angular/core';
import type { AvailableTransitionModel, WorkflowData, WorkflowInstanceModel } from '@middt/ui-core';

/**
 * Renders a registered workflow view (see WORKFLOW_VIEWS) with live input bindings: state views get `instance`/`interactive`,
 * transition views additionally `transition`, `errors`, `busy` and raise `submitted`/`cancelled`.
 * The view is inserted next to this element.
 */
@Component({
  selector: 'middt-wf-view-outlet',
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: '',
  host: { style: 'display: none' },
})
export class MiddtWorkflowViewOutletComponent {
  readonly component = input.required<Type<unknown>>();
  readonly kind = input<'state' | 'transition'>('state');
  readonly instance = input.required<WorkflowInstanceModel>();
  readonly interactive = input(true);
  readonly transition = input<AvailableTransitionModel | null>(null);
  readonly errors = input<readonly string[]>([]);
  readonly busy = input(false);
  readonly submitted = output<WorkflowData>();
  readonly cancelled = output<void>();

  readonly #container: ViewContainerRef;

  constructor(container: ViewContainerRef) {
    this.#container = container;
    effect(() => {
      const type = this.component();
      const kind = this.kind();
      untracked(() => this.#render(type, kind));
    });
  }

  #render(type: Type<unknown>, kind: 'state' | 'transition'): void {
    this.#container.clear();
    const bindings: Binding[] =
      kind === 'state'
        ? [inputBinding('instance', this.instance), inputBinding('interactive', this.interactive)]
        : [
            inputBinding('instance', this.instance),
            inputBinding('transition', this.transition),
            inputBinding('errors', this.errors),
            inputBinding('busy', this.busy),
            outputBinding<WorkflowData>('submitted', body => this.submitted.emit(body)),
            outputBinding<void>('cancelled', () => this.cancelled.emit()),
          ];
    this.#container.createComponent(type, { bindings });
  }
}
