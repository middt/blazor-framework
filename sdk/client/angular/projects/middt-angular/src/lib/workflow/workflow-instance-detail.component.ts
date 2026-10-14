import {
  afterNextRender,
  ChangeDetectionStrategy,
  Component,
  computed,
  DestroyRef,
  effect,
  ElementRef,
  inject,
  Injector,
  input,
  output,
  signal,
  untracked,
  viewChild,
  ViewEncapsulation,
} from '@angular/core';
import { MatButtonModule } from '@angular/material/button';
import { MatCardModule } from '@angular/material/card';
import { MatIconModule } from '@angular/material/icon';
import {
  errorText,
  formatWorkflowDate,
  formatWorkflowJson,
  isSuccess,
  schemaHasInput,
  workflowHistoryPointStyle,
  workflowIsPending,
  workflowLayoutVariables,
  workflowStateTitle,
  workflowStatusBadge,
  workflowStatusText,
  workflowStepClass,
  workflowStepPrefill,
  workflowStepStatusText,
  workflowStepTransition,
  workflowSubTypeText,
  workflowTransitionIcon,
  workflowTransitionStyle,
  workflowTransitionTitle,
  workflowTriggerText,
  workflowWizardSteps,
} from '@middt/ui-core';
import type {
  AvailableTransitionModel,
  BaseResponseDataModel,
  WorkflowData,
  WorkflowDefinitionModel,
  WorkflowHistoryModel,
  WorkflowIncidentModel,
  WorkflowInstanceModel,
} from '@middt/ui-core';
import { NotifyService } from '../core/notify.service';
import { MiddtLayoutHostComponent } from '../ui/layout-host.component';
import { MiddtJsonSchemaFormComponent } from './json-schema-form.component';
import { WORKFLOW_API_CLIENT } from './workflow-client';
import { MiddtWorkflowDialogComponent } from './workflow-dialog.component';
import { WF_SHARED_STYLES } from './workflow-styles';
import { MiddtWorkflowViewOutletComponent } from './workflow-view-outlet.component';
import { findWorkflowView, WORKFLOW_VIEWS } from './workflow-views';

const POLL_ATTEMPTS = 15;
const POLL_INTERVAL_MS = 1000;

/**
 * One workflow instance (counterpart of WorkflowInstanceDetail.razor): current state, the transitions the user may execute
 * (input forms built from their JSON Schema or registered transition views), scheduled transitions, history and incidents.
 * Wizard workflows (states with a `step`) get a step indicator, and an `inline` transition is rendered as the step form inside
 * the screen. The state is shown by its registered Angular view, else its JSON layout, else the server rendered view
 * (`GET …/view`), else as raw JSON.
 */
@Component({
  selector: 'middt-workflow-instance-detail',
  imports: [
    MatCardModule, MatButtonModule, MatIconModule, MiddtJsonSchemaFormComponent, MiddtWorkflowDialogComponent,
    MiddtWorkflowViewOutletComponent, MiddtLayoutHostComponent,
  ],
  encapsulation: ViewEncapsulation.None,
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './workflow-instance-detail.component.html',
  styleUrl: './workflow-instance-detail.component.css',
  styles: WF_SHARED_STYLES,
})
export class MiddtWorkflowInstanceDetailComponent {
  /** Workflow key (e.g. `order-approval`). */
  readonly workflow = input.required<string>();
  /** Instance to show; null shows a hint to select one. */
  readonly instanceId = input<string | null>(null);
  /** Prefix of element ids (unique per page when several details are shown). */
  readonly idPrefix = input('wf');
  /** Raised after a transition or retry changed the instance (or polling picked up the next step). */
  readonly changed = output<WorkflowInstanceModel>();

  protected readonly instance = signal<WorkflowInstanceModel | null>(null);
  readonly #definitions = signal<ReadonlyMap<number, WorkflowDefinitionModel>>(new Map());
  protected readonly history = signal<WorkflowHistoryModel[]>([]);
  protected readonly incidents = signal<WorkflowIncidentModel[]>([]);
  protected readonly loadFailed = signal(false);
  protected readonly retrying = signal(false);
  protected readonly executing = signal(false);
  protected readonly pendingTransition = signal<AvailableTransitionModel | null>(null);
  protected readonly transitionErrors = signal<string[]>([]);
  protected readonly stepErrors = signal<string[]>([]);
  /** Server rendered HTML of the state view (when no Angular view is registered for it). */
  protected readonly serverHtml = signal<string | null>(null);

  readonly #views = inject(WORKFLOW_VIEWS);
  readonly #client = inject(WORKFLOW_API_CLIENT);
  readonly #notify = inject(NotifyService);
  readonly #injector = inject(Injector);
  #abort = new AbortController();
  #loadedWorkflow: string | null = null;
  #loadedId: string | null = null;
  #polling = false;
  #destroyed = false;

  private readonly stepForm = viewChild<MiddtJsonSchemaFormComponent>('stepForm');
  private readonly transitionForm = viewChild<MiddtJsonSchemaFormComponent>('transitionForm');
  private readonly stepViewElement = viewChild<ElementRef<HTMLElement>>('stepViewElement');
  private readonly stateHeading = viewChild<ElementRef<HTMLElement>>('stateHeading');

  /** Definition of the instance's version (instances keep running on the version they were started with). */
  protected readonly definition = computed(() => {
    const instance = this.instance();
    return instance === null ? null : (this.#definitions().get(instance.workflowVersion) ?? null);
  });

  protected readonly steps = computed(() => workflowWizardSteps(this.definition(), this.instance(), this.history()));

  /** The inline transition rendered as the wizard step form. */
  protected readonly stepTransition = computed(() => workflowStepTransition(this.instance()));
  protected readonly stepPrefill = computed(() => {
    const step = this.stepTransition();
    return step === null ? null : workflowStepPrefill(this.instance()?.data, step.inputInto, step.inputSchema);
  });
  /** Recreates the step form per instance, state, data version and transition (like `@key` in Blazor). */
  protected readonly stepFormKey = computed(() => {
    const instance = this.instance();
    const step = this.stepTransition();
    return instance === null || step === null ? [] : [`${instance.id}:${instance.state}:${instance.dataVersion}:${step.key}`];
  });

  protected readonly stateView = computed(() => findWorkflowView(this.#views, this.instance()?.view, 'state'));
  protected readonly stepViewType = computed(() => findWorkflowView(this.#views, this.stepTransition()?.view, 'transition'));
  protected readonly transitionView = computed(() => findWorkflowView(this.#views, this.pendingTransition()?.view, 'transition'));
  protected readonly pendingHasInput = computed(() => schemaHasInput(this.pendingTransition()?.inputSchema));

  /** Transitions shown as buttons: all available ones except the inline step transition. */
  protected readonly buttonTransitions = computed(() => {
    const step = this.stepTransition();
    return (this.instance()?.availableTransitions ?? []).filter(t => t !== step);
  });
  /** Next to a step form, transitions without input (e.g. "Geri") run directly; others open their dialog. */
  protected readonly directTransitions = computed(() => this.buttonTransitions().filter(t => this.#runsDirectly(t)));
  protected readonly dialogTransitions = computed(() => this.buttonTransitions().filter(t => !this.#runsDirectly(t)));

  protected readonly layoutVariables = computed(() => {
    const instance = this.instance();
    return instance === null ? {} : workflowLayoutVariables(instance);
  });
  protected readonly layoutKeyId = computed(() => {
    const instance = this.instance();
    return instance?.layout ? [`${instance.id}:${instance.dataVersion}:${instance.layout}`] : [];
  });
  protected readonly dataJson = computed(() => formatWorkflowJson(this.instance()?.data));

  protected readonly date = formatWorkflowDate;
  protected readonly statusText = workflowStatusText;
  protected readonly badge = workflowStatusBadge;
  protected readonly subTypeText = workflowSubTypeText;
  protected readonly triggerText = workflowTriggerText;
  protected readonly stepClass = workflowStepClass;
  protected readonly stepStatusText = workflowStepStatusText;

  constructor() {
    inject(DestroyRef).onDestroy(() => {
      this.#destroyed = true;
      this.#abort.abort();
    });
    effect(() => {
      this.workflow();
      this.instanceId();
      untracked(() => void this.refresh());
    });
  }

  /** Reloads the instance, its history and incidents. */
  async refresh(): Promise<void> {
    try {
      await this.#load();
    } catch {
      // aborted (another instance selected or component destroyed)
    }
  }

  protected stateTitle(state: string | null | undefined): string {
    return workflowStateTitle(this.definition(), state);
  }

  protected transitionTitle(key: string): string {
    return workflowTransitionTitle(this.definition(), key);
  }

  protected transitionText(transition: AvailableTransitionModel): string {
    return transition.title ?? transition.key;
  }

  protected transitionStyle(transition: AvailableTransitionModel): string {
    return workflowTransitionStyle(this.definition(), transition);
  }

  protected transitionIcon(transition: AvailableTransitionModel): string {
    return workflowTransitionIcon(this.definition(), transition);
  }

  protected pointStyle(entry: WorkflowHistoryModel): string {
    return workflowHistoryPointStyle(this.definition(), entry);
  }

  /**
   * Loads the instance. `details` also loads its history and incidents in the same round trip; without it (polling) only the
   * instance is fetched while it is pending, and history, incidents and the server view are loaded once it settles.
   */
  async #load(details = true): Promise<void> {
    const workflow = this.workflow();
    const id = this.instanceId();
    if (workflow !== this.#loadedWorkflow) {
      this.#definitions.set(new Map());
      this.#loadedWorkflow = workflow;
    }

    if (id !== this.#loadedId) {
      this.#abort.abort();
      this.#abort = new AbortController();
      this.instance.set(null);
      this.history.set([]);
      this.incidents.set([]);
      this.serverHtml.set(null);
      this.stepErrors.set([]);
      this.pendingTransition.set(null);
      details = true;
    }

    this.#loadedId = id;
    this.loadFailed.set(false);
    if (id === null) {
      return;
    }

    const signal = this.#abort.signal;
    const detailRequests = (): Promise<[BaseResponseDataModel<WorkflowHistoryModel[]>, BaseResponseDataModel<WorkflowIncidentModel[]>]> =>
      Promise.all([this.#client.getHistory(workflow, id, signal), this.#client.getIncidents(workflow, id, signal)]);
    const [instanceResponse, early] = await Promise.all([
      this.#client.getInstance(workflow, id, signal),
      details ? detailRequests() : Promise.resolve(null),
    ]);
    if (signal.aborted || id !== this.instanceId()) {
      return;
    }

    const instance = instanceResponse.data;
    if (!isSuccess(instanceResponse) || !instance) {
      this.instance.set(null);
      this.loadFailed.set(true);
      this.#notify.error('Kayıt yüklenemedi', errorText(instanceResponse));
      return;
    }

    const settled = details || !workflowIsPending(instance);
    const [detailResponses, serverHtml] = await Promise.all([
      early !== null ? Promise.resolve(early) : settled ? detailRequests() : Promise.resolve(null),
      this.#ensureDefinition(workflow, instance.workflowVersion, signal).then(() => (settled ? this.#loadServerHtml(workflow, instance, signal) : undefined)),
    ]);
    if (signal.aborted || id !== this.instanceId()) {
      return;
    }

    if (detailResponses !== null) {
      const [historyResponse, incidentsResponse] = detailResponses;
      this.history.set(isSuccess(historyResponse) ? (historyResponse.data ?? []) : []);
      this.incidents.set(isSuccess(incidentsResponse) ? (incidentsResponse.data ?? []) : []);
    }

    if (serverHtml !== undefined) {
      this.serverHtml.set(serverHtml);
    } else if (this.instance()?.state !== instance.state) {
      this.serverHtml.set(null);
    }

    this.stepErrors.set([]);
    this.instance.set(instance);
  }

  async #ensureDefinition(workflow: string, version: number, signal: AbortSignal): Promise<void> {
    if (this.#definitions().has(version)) {
      return;
    }

    const response = await this.#client.getDefinition(workflow, version, signal);
    if (isSuccess(response) && response.data) {
      const definition = response.data;
      this.#definitions.update(map => new Map(map).set(version, definition));
    }
  }

  /** The state has a view but no Angular view is registered (and no layout): use the server rendered HTML. */
  async #loadServerHtml(workflow: string, instance: WorkflowInstanceModel, signal: AbortSignal): Promise<string | null> {
    if (!instance.view || instance.layout || findWorkflowView(this.#views, instance.view, 'state') !== null) {
      return null;
    }

    const response = await this.#client.renderView(workflow, instance.id, signal);
    return isSuccess(response) && response.data ? response.data : null;
  }

  #runsDirectly(transition: AvailableTransitionModel): boolean {
    return this.stepTransition() !== null && !schemaHasInput(transition.inputSchema);
  }

  protected onTransitionClick(transition: AvailableTransitionModel): void {
    if (this.#runsDirectly(transition)) {
      if (!this.executing()) {
        void this.#execute(transition, null, true);
      }

      return;
    }

    this.transitionErrors.set([]);
    this.pendingTransition.set(transition);
  }

  protected closeTransition(): void {
    if (!this.executing()) {
      this.pendingTransition.set(null);
    }
  }

  protected executePending(): void {
    const transition = this.pendingTransition();
    if (transition === null || this.instance() === null) {
      return;
    }

    const hasInput = this.pendingHasInput();
    const form = this.transitionForm();
    if (hasInput && form !== undefined && !form.validate()) {
      return;
    }

    void this.#execute(transition, hasInput ? (form?.getValue() ?? null) : null, false);
  }

  protected submitStep(): void {
    const transition = this.stepTransition();
    if (transition === null || this.executing()) {
      return;
    }

    const hasInput = schemaHasInput(transition.inputSchema);
    const form = this.stepForm();
    if (hasInput && form !== undefined && !form.validate()) {
      return;
    }

    void this.#execute(transition, hasInput ? (form?.getValue() ?? null) : null, true);
  }

  protected submitCustomForm(body: WorkflowData): void {
    const transition = this.pendingTransition();
    if (transition !== null && !this.executing()) {
      void this.#execute(transition, body, false);
    }
  }

  protected submitCustomStep(body: WorkflowData): void {
    const transition = this.stepTransition();
    if (transition !== null && !this.executing()) {
      void this.#execute(transition, body, true);
    }
  }

  protected clearStepErrors(): void {
    this.stepErrors.set([]);
  }

  /**
   * Executes `transition`. `fromStep`: started from the wizard step form (inline submit or a direct "Geri"); errors are shown
   * inside the step form (entered values stay) instead of the dialog.
   */
  async #execute(transition: AvailableTransitionModel, input: WorkflowData | null, fromStep: boolean): Promise<void> {
    const instance = this.instance();
    if (instance === null) {
      return;
    }

    this.executing.set(true);
    this.transitionErrors.set([]);
    this.stepErrors.set([]);
    try {
      const response = await this.#client.executeTransition(this.workflow(), instance.id, transition.key, input, false, this.#abort.signal);
      if (!isSuccess(response) || !response.data) {
        const errors = response.messageList.length > 0 ? response.messageList : ['İşlem uygulanamadı.'];
        if (fromStep) {
          this.stepErrors.set(errors);
        } else {
          this.transitionErrors.set(errors);
          this.#notify.error(`${this.transitionText(transition)} uygulanamadı`, errors.join(' '));
        }

        return;
      }

      this.pendingTransition.set(null);
      if (!fromStep) {
        this.#notify.success(`${this.transitionText(transition)} uygulandı`, `Yeni adım: ${response.data.stateTitle ?? response.data.state}`);
      }

      await this.#load();
      this.changed.emit(this.instance() ?? response.data);
      if (fromStep) {
        this.#focusStep();
      }
    } catch {
      // aborted
    } finally {
      this.executing.set(false);
    }

    this.#startPollingIfPending();
  }

  protected async retry(): Promise<void> {
    const instance = this.instance();
    if (instance === null) {
      return;
    }

    this.retrying.set(true);
    try {
      const response = await this.#client.retry(this.workflow(), instance.id, this.#abort.signal);
      if (!isSuccess(response) || !response.data) {
        this.#notify.error('Tekrar deneme başarısız', errorText(response));
        return;
      }

      this.#notify.success('Tekrar denendi', `Durum: ${workflowStatusText(response.data.status)}`);
      await this.#load();
      this.changed.emit(this.instance() ?? response.data);
    } catch {
      // aborted
    } finally {
      this.retrying.set(false);
    }
  }

  /**
   * While the instance is busy or waits for a subflow (e.g. right after a step was submitted), reloads it a few times so the
   * next step appears without the user pressing "Yenile".
   */
  #startPollingIfPending(): void {
    const instance = this.instance();
    if (this.#polling || !workflowIsPending(instance)) {
      return;
    }

    this.#polling = true;
    void this.#poll(instance!.id).finally(() => (this.#polling = false));
  }

  async #poll(id: string): Promise<void> {
    for (let attempt = 0; attempt < POLL_ATTEMPTS; attempt++) {
      await new Promise(resolve => setTimeout(resolve, POLL_INTERVAL_MS));
      if (this.#destroyed || this.instance()?.id !== id || !workflowIsPending(this.instance())) {
        return;
      }

      if (this.executing()) {
        continue;
      }

      try {
        await this.#load(false); // only the instance while it is pending; history and incidents once it settles
      } catch {
        return;
      }

      const instance = this.instance();
      if (instance !== null && instance.id === id && !workflowIsPending(instance)) {
        this.changed.emit(instance);
        this.#focusStep();
        return;
      }
    }
  }

  /** After a step: focus the first field of the next step form (or the custom step view / the state heading) for keyboard users. */
  #focusStep(): void {
    afterNextRender(
      () => {
        const form = this.stepForm();
        if (this.stepTransition() !== null && this.stepViewType() === null && form?.hasFields()) {
          form.focusFirst();
        } else if (this.stepTransition() !== null && this.stepViewType() !== null) {
          this.stepViewElement()?.nativeElement.focus();
        } else {
          this.stateHeading()?.nativeElement.focus();
        }
      },
      { injector: this.#injector },
    );
  }
}
