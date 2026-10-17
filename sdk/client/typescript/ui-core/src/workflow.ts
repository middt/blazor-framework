// Workflow API contracts (Middt.Framework.Model/Model/Workflow/WorkflowModels.cs, camelCase JSON) and a fetch based client
// of the endpoints mapped by `MapMiddtWorkflows` (Middt.Framework.Workflow/Endpoints/WorkflowEndpoints.cs), mirroring
// Middt.Framework.Client/Http/WorkflowApiClient.cs. Like the other clients, failures (ProblemDetails 400/403/404/409,
// network errors) never throw: they come back as `result: -1` with the server messages in `messageList`.

import { ApiClientBase, pathSegment, UNAVAILABLE_MESSAGE } from './client.ts';
import type { ApiClientOptions } from './client.ts';
import { ResultEnum } from './types.ts';
import type { BaseResponseDataModel } from './types.ts';

/** JSON object of workflow data / inputs / schemas. */
export type WorkflowData = Record<string, unknown>;

/** `WorkflowInstanceStatus` (serialized as its name). */
export const WorkflowInstanceStatus = {
  /** Waiting for the next transition (or for a subflow). */
  active: 'Active',
  /** A transition is executing; further transitions are rejected with 409. */
  busy: 'Busy',
  completed: 'Completed',
  /** A task failed; see incidents and use retry. */
  faulted: 'Faulted',
  cancelled: 'Cancelled',
} as const;

export type WorkflowInstanceStatusName = (typeof WorkflowInstanceStatus)[keyof typeof WorkflowInstanceStatus];

/** All statuses in declaration order (status filters). */
export const WORKFLOW_INSTANCE_STATUSES: readonly WorkflowInstanceStatusName[] = Object.freeze(Object.values(WorkflowInstanceStatus));

export interface StartWorkflowRequest {
  /** Optional business key (unique per workflow); starting twice with the same key returns the existing instance. */
  key?: string | null;
  /** Validated against the start transition's input schema. */
  data?: WorkflowData | null;
}

export interface WorkflowSummaryModel {
  key: string;
  version: number;
  title?: string | null;
}

export interface WorkflowStateModel {
  key: string;
  title?: string | null;
  /** `Start`, `Normal`, `SubFlow`, `Finish`, … */
  type: string;
  /** `None`, `Human`, `Success`, `Error`, `Cancelled`, `Timeout`. */
  subType: string;
  /** Name of the registered view component for this state (server driven UI), if any. */
  view?: string | null;
  /** Key of the JSON layout (server driven UI) shown for this state, if any. */
  layout?: string | null;
  /** Wizard step number (progress indicator), if the state is a step. */
  step?: number | null;
}

export interface WorkflowTransitionModel {
  key: string;
  title?: string | null;
  /** Source state; null for shared transitions. */
  from?: string | null;
  target: string;
  /** `Manual`, `Automatic`, `Scheduled`, … */
  trigger: string;
}

/** States and transitions of a workflow (e.g. for diagrams). */
export interface WorkflowDefinitionModel {
  key: string;
  version: number;
  title?: string | null;
  startState: string;
  startInputSchema?: WorkflowData | null;
  states: WorkflowStateModel[];
  transitions: WorkflowTransitionModel[];
}

/** A manual transition the caller may execute in the instance's current state. */
export interface AvailableTransitionModel {
  key: string;
  title?: string | null;
  target: string;
  /** JSON Schema of the expected request body, when the transition takes input. */
  inputSchema?: WorkflowData | null;
  /** Name of a registered input form component that replaces the schema generated form, if any. */
  view?: string | null;
  /** Render the input form inside the instance screen (wizard step) instead of a dialog. */
  inline?: boolean;
  /** Data path the input is stored under (lets UIs pre-fill the form when a step is revisited). */
  inputInto?: string | null;
}

export interface ScheduledTransitionModel {
  key: string;
  /** ISO timestamp (UTC). */
  dueAt: string;
}

export interface WorkflowInstanceModel {
  id: string;
  key?: string | null;
  workflow: string;
  workflowVersion: number;
  state: string;
  stateTitle?: string | null;
  stateType: string;
  stateSubType: string;
  /** Name of the view component of the current state (server driven UI), if any. */
  view?: string | null;
  /** Key of the JSON layout of the current state (rendered with `data` and `instance`), if any. */
  layout?: string | null;
  status: WorkflowInstanceStatusName;
  data: WorkflowData;
  dataVersion: number;
  parentInstanceId?: string | null;
  waitingForInstanceId?: string | null;
  error?: string | null;
  startedBy?: string | null;
  createdAt: string;
  updatedAt: string;
  completedAt?: string | null;
  availableTransitions: AvailableTransitionModel[];
  scheduledTransitions: ScheduledTransitionModel[];
  /** Projected state and available transitions of the active child subflow when `waitingForInstanceId` is set. */
  activeSubFlow?: WorkflowInstanceModel | null;
}

export interface WorkflowTaskExecutionModel {
  taskKey: string;
  taskType: string;
  hook: string;
  attempts: number;
  durationMs: number;
  outcome: string;
}

export interface WorkflowHistoryModel {
  transitionKey: string;
  fromState?: string | null;
  toState: string;
  trigger: string;
  executedBy?: string | null;
  startedAt: string;
  finishedAt: string;
  dataVersion: number;
  tasks?: WorkflowTaskExecutionModel[];
}

export interface WorkflowIncidentModel {
  id: number;
  state?: string | null;
  transitionKey?: string | null;
  taskKey?: string | null;
  error: string;
  attempts: number;
  createdAt: string;
  resolvedAt?: string | null;
}

/** Filter and paging of `listInstances` (defaults: page 1, 20 per page). */
export interface WorkflowInstanceQuery {
  state?: string | null;
  status?: WorkflowInstanceStatusName | null;
  key?: string | null;
  page?: number;
  pageSize?: number;
}

export interface WorkflowApiClientOptions extends ApiClientOptions {
  /** Route prefix of `MapMiddtWorkflows` (default `api/workflows`). */
  routePrefix?: string;
}

const isAbort = (error: unknown): boolean => error instanceof Error && error.name === 'AbortError';

function query(values: Record<string, string | number | null | undefined>): string {
  const parts: string[] = [];
  for (const [name, value] of Object.entries(values)) {
    if (value !== undefined && value !== null && value !== '') {
      parts.push(`${name}=${encodeURIComponent(String(value))}`);
    }
  }

  return parts.length === 0 ? '' : `?${parts.join('&')}`;
}

/** Client of the workflow REST API mapped by `MapMiddtWorkflows` (default prefix `api/workflows`). */
export class WorkflowApiClient extends ApiClientBase {
  readonly routePrefix: string;

  constructor(options: WorkflowApiClientOptions = {}) {
    super(options);
    this.routePrefix = (options.routePrefix ?? 'api/workflows').replace(/^\/+|\/+$/g, '');
  }

  /** Workflows the user may access (latest versions). */
  listWorkflows(signal?: AbortSignal): Promise<BaseResponseDataModel<WorkflowSummaryModel[]>> {
    return this.send('GET', this.routePrefix, undefined, signal);
  }

  /**
   * Definition of `workflow`: the latest version, or `version` when given (instances keep running on the version they were
   * started with, see `WorkflowInstanceModel.workflowVersion`).
   */
  getDefinition(workflow: string, version?: number | null, signal?: AbortSignal): Promise<BaseResponseDataModel<WorkflowDefinitionModel>> {
    const suffix = version === undefined || version === null ? '' : query({ version: Math.trunc(version) });
    return this.send('GET', `${this.workflowRoute(workflow)}${suffix}`, undefined, signal);
  }

  /** Starts an instance (201, or 200 with the existing instance when the business key is already used). */
  start(workflow: string, request: StartWorkflowRequest = {}, signal?: AbortSignal): Promise<BaseResponseDataModel<WorkflowInstanceModel>> {
    return this.send('POST', `${this.workflowRoute(workflow)}/instances`, request, signal);
  }

  /** One page of instances; `count` is the total count. */
  listInstances(workflow: string, filter: WorkflowInstanceQuery = {}, signal?: AbortSignal): Promise<BaseResponseDataModel<WorkflowInstanceModel[]>> {
    const suffix = query({
      state: filter.state,
      status: filter.status,
      key: filter.key,
      page: Math.trunc(filter.page ?? 1),
      pageSize: Math.trunc(filter.pageSize ?? 20),
    });
    return this.send('GET', `${this.workflowRoute(workflow)}/instances${suffix}`, undefined, signal);
  }

  /** Instance including the transitions the current user may execute. */
  getInstance(workflow: string, id: string, signal?: AbortSignal): Promise<BaseResponseDataModel<WorkflowInstanceModel>> {
    return this.send('GET', this.instanceRoute(workflow, id), undefined, signal);
  }

  /** Executes a manual transition; with `runAsync` the transition is queued (202). */
  executeTransition(
    workflow: string,
    id: string,
    transition: string,
    input?: WorkflowData | null,
    runAsync = false,
    signal?: AbortSignal,
  ): Promise<BaseResponseDataModel<WorkflowInstanceModel>> {
    if (transition === '') {
      throw new Error('Transition key is required.');
    }

    const path = `${this.instanceRoute(workflow, id)}/transitions/${pathSegment(transition, 'Transition key')}${runAsync ? '?async=true' : ''}`;
    return this.send('POST', path, input ?? undefined, signal);
  }

  getHistory(workflow: string, id: string, signal?: AbortSignal): Promise<BaseResponseDataModel<WorkflowHistoryModel[]>> {
    return this.send('GET', `${this.instanceRoute(workflow, id)}/history`, undefined, signal);
  }

  getIncidents(workflow: string, id: string, signal?: AbortSignal): Promise<BaseResponseDataModel<WorkflowIncidentModel[]>> {
    return this.send('GET', `${this.instanceRoute(workflow, id)}/incidents`, undefined, signal);
  }

  /** Retries the failed step of a `Faulted` instance. */
  retry(workflow: string, id: string, signal?: AbortSignal): Promise<BaseResponseDataModel<WorkflowInstanceModel>> {
    return this.send('POST', `${this.instanceRoute(workflow, id)}/retry`, undefined, signal);
  }

  /**
   * Server rendered HTML of the current state's view (`GET …/instances/{id}/view`, the Razor view rendered to static HTML).
   * `data` is the HTML fragment; a state without a registered view is a 404 failure. Insert it only through a sanitizer.
   */
  async renderView(workflow: string, id: string, signal?: AbortSignal): Promise<BaseResponseDataModel<string>> {
    const path = `${this.instanceRoute(workflow, id)}/view`; // invalid keys throw (rejected promise), like the other methods
    try {
      const response = await this.sendRaw('GET', path, signal);
      if (!response.ok) {
        return await ApiClientBase.readResponse<BaseResponseDataModel<string>>(response);
      }

      return { result: ResultEnum.success, messageList: [], count: 1, data: await response.text() };
    } catch (error) {
      if (isAbort(error) && signal?.aborted) {
        throw error;
      }

      return { result: ResultEnum.error, messageList: [UNAVAILABLE_MESSAGE], count: 0, data: null };
    }
  }

  protected workflowRoute(workflow: string): string {
    if (workflow === '') {
      throw new Error('Workflow key is required.');
    }

    return `${this.routePrefix}/${pathSegment(workflow, 'Workflow key')}`;
  }

  protected instanceRoute(workflow: string, id: string): string {
    if (id === '') {
      throw new Error('Instance id is required.');
    }

    return `${this.workflowRoute(workflow)}/instances/${pathSegment(id, 'Instance id')}`;
  }
}
