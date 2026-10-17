import { computed, Directive, EnvironmentProviders, InjectionToken, input, makeEnvironmentProviders, output, Type } from '@angular/core';
import { workflowViewDate, workflowViewNumber, workflowViewText } from '@middt/ui-core';
import type { AvailableTransitionModel, WorkflowData, WorkflowInstanceModel } from '@middt/ui-core';

/**
 * Registry entry of an Angular workflow view (counterpart of a `[WorkflowView("name")]` Razor view). Workflow definitions
 * reference views by name: `"view": { "component": "name" }` on a state (screen of the instance) or on a transition (input form).
 */
export interface WorkflowViewRegistration {
  name: string;
  /** 'state' views show an instance (extend {@link WorkflowStateViewBase}); 'transition' views are custom input forms (extend {@link WorkflowTransitionViewBase}). */
  kind: 'state' | 'transition';
  component: Type<unknown>;
}

export const WORKFLOW_VIEWS = new InjectionToken<WorkflowViewRegistration[]>('WORKFLOW_VIEWS', { factory: () => [] });

/** Registers the Angular workflow views of an application: `provideWorkflowViews([{ name: 'order-review', kind: 'state', component: OrderReviewView }])`. */
export function provideWorkflowViews(views: readonly WorkflowViewRegistration[]): EnvironmentProviders {
  const names = new Set<string>();
  for (const view of views) {
    if (names.has(view.name)) {
      throw new Error(`Workflow view '${view.name}' is registered twice.`);
    }

    names.add(view.name);
  }

  return makeEnvironmentProviders([{ provide: WORKFLOW_VIEWS, useValue: [...views] }]);
}

/** Registered view of `kind` named `name`; null when there is none. */
export function findWorkflowView(views: readonly WorkflowViewRegistration[], name: string | null | undefined, kind: 'state' | 'transition'): Type<unknown> | null {
  if (name === null || name === undefined || name === '') {
    return null;
  }

  return views.find(v => v.name === name && v.kind === kind)?.component ?? null;
}

/**
 * Base class of state views (counterpart of `WorkflowViewBase`): `instance` and `interactive` inputs plus data helpers
 * (`text('credit.score')`, `number('amount')`, `date('requestedAt')`). Data is output through Angular bindings (escaped).
 */
@Directive()
export abstract class WorkflowStateViewBase {
  readonly instance = input.required<WorkflowInstanceModel>();

  /** `false` when the view is shown read-only (no actions next to it). */
  readonly interactive = input(true);

  protected readonly data = computed(() => this.instance().data);

  /** Value at a dot path (`credit.score`) as display text, or `fallback`. */
  protected text(path: string, fallback = '-'): string {
    return workflowViewText(this.data(), path, fallback);
  }

  protected number(path: string): number | null {
    return workflowViewNumber(this.data(), path);
  }

  protected date(path: string): Date | null {
    return workflowViewDate(this.data(), path);
  }
}

/**
 * Base class of transition views (counterpart of `WorkflowTransitionViewBase`): a custom input form that replaces the schema
 * generated form. Call `submit(body)` with the request body; API validation errors come back in `errors`.
 */
@Directive()
export abstract class WorkflowTransitionViewBase {
  readonly instance = input.required<WorkflowInstanceModel>();
  readonly transition = input.required<AvailableTransitionModel>();
  readonly errors = input<readonly string[]>([]);
  readonly busy = input(false);

  /** Request body of the transition. */
  readonly submitted = output<WorkflowData>();
  readonly cancelled = output<void>();

  protected readonly data = computed(() => this.instance().data);

  protected submit(body: WorkflowData): void {
    this.submitted.emit(body);
  }

  protected cancel(): void {
    this.cancelled.emit();
  }
}
