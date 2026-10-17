import { ChangeDetectionStrategy, Component, computed, effect, inject, input, linkedSignal, signal, viewChild } from '@angular/core';
import { toSignal } from '@angular/core/rxjs-interop';
import { Title } from '@angular/platform-browser';
import { ActivatedRoute, Router } from '@angular/router';
import type { Params } from '@angular/router';
import { MiddtWorkflowInstanceDetailComponent, MiddtWorkflowInstanceListComponent } from '@middt/angular/workflow';
import type { WorkflowStartOpened } from '@middt/angular/workflow';
import { workflowStartQuery, WORKFLOW_START_KEY_PARAM, WORKFLOW_START_QUERY_PARAM } from '@middt/ui-core';
import type { WorkflowDefinitionModel, WorkflowInstanceModel } from '@middt/ui-core';

/** List headings kept from the former per-workflow pages (the E2E suites check them); others use the workflow title. */
const LIST_TITLES: Readonly<Record<string, string>> = { 'order-approval': 'Sipariş onayları' };

/**
 * Any workflow (`/workflow/:key`): instance list and detail side by side (counterpart of WorkflowPage.razor). The browser
 * title is the workflow title; `?start=1` opens the start dialog once (when the user may start), `key` prefills its business
 * key and other parameters named like start input properties prefill the start form (e.g.
 * `?start=1&key=category-5&categoryId=5`); these parameters are then removed from the URL.
 */
@Component({
  selector: 'app-workflow',
  imports: [MiddtWorkflowInstanceListComponent, MiddtWorkflowInstanceDetailComponent],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <h1 class="page-title sr-only">{{ heading() }}</h1>
    <div class="middt-wf-layout">
      <middt-workflow-instance-list #list [workflow]="key()" [title]="listTitle()" [selectedId]="selectedId()" [openStart]="openStart()"
                                    [startKey]="startQuery().key" [startValues]="startQuery().values"
                                    (startOpened)="onStartOpened($event)" (definitionLoaded)="onDefinitionLoaded($event)" (selected)="onSelected($event)" />
      <middt-workflow-instance-detail [workflow]="key()" [instanceId]="selectedId()" (changed)="onChanged()" />
    </div>
  `,
  styles: `
    .middt-wf-layout {
      display: grid;
      /* the detail column hosts wizard step forms: keep it at least ~460px wide next to the list */
      grid-template-columns: minmax(0, 1fr) minmax(min(100%, 460px), 1fr);
      gap: 1rem;
      align-items: start;
    }

    @media (max-width: 1100px) {
      .middt-wf-layout { grid-template-columns: minmax(0, 1fr); }
    }
  `,
})
export class WorkflowPage {
  /** Workflow key (route parameter). */
  readonly key = input.required<string>();
  protected readonly selectedId = linkedSignal<string, string | null>({ source: this.key, computation: () => null });
  readonly #route = inject(ActivatedRoute);
  readonly #queryParams = toSignal(this.#route.queryParams, { initialValue: {} as Params });
  /** The start link of the query (`start`, `key`, start input values). */
  protected readonly startQuery = computed(() => workflowStartQuery(this.#queryParams()));
  protected readonly openStart = computed(() => this.startQuery().open);
  /** null: the list shows the workflow definition title. */
  protected readonly listTitle = computed(() => LIST_TITLES[this.key()] ?? null);
  readonly #definitionTitle = signal<{ key: string; title: string } | null>(null);
  protected readonly heading = computed(() => {
    const loaded = this.#definitionTitle();
    return this.listTitle() ?? (loaded?.key === this.key() ? loaded.title : this.key());
  });

  private readonly list = viewChild.required<MiddtWorkflowInstanceListComponent>('list');
  readonly #title = inject(Title);
  readonly #router = inject(Router);

  constructor() {
    // Until the definition is loaded the browser title is the key (the route has no static title).
    effect(() => this.#title.setTitle(this.key()));
  }

  protected onDefinitionLoaded(definition: WorkflowDefinitionModel): void {
    if (definition.key === this.key()) {
      const title = definition.title || definition.key;
      this.#definitionTitle.set({ key: definition.key, title });
      this.#title.setTitle(title);
    }
  }

  protected onSelected(instance: WorkflowInstanceModel): void {
    this.selectedId.set(instance.id);
  }

  // A transition changes state/status in the list as well.
  protected onChanged(): void {
    void this.list().refresh();
  }

  /** The dialog opened from `?start=1`: drop the start link parameters so a reload or "back" does not open it again. */
  protected onStartOpened(opened: WorkflowStartOpened): void {
    const queryParams: Params = { [WORKFLOW_START_QUERY_PARAM]: null, [WORKFLOW_START_KEY_PARAM]: null };
    for (const name of opened.names) {
      queryParams[name] = null;
    }

    void this.#router.navigate([], { relativeTo: this.#route, queryParams, queryParamsHandling: 'merge', replaceUrl: true });
  }
}
