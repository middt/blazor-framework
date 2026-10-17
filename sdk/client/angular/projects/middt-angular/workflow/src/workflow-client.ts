import { EnvironmentProviders, inject, InjectionToken, makeEnvironmentProviders } from '@angular/core';
import { WorkflowApiClient } from '@middt/ui-core';
import { bffFetch } from '@middt/angular/core';
import { MIDDT_CONFIG } from '@middt/angular/core';

/** Settings of the workflow screens. */
export interface MiddtWorkflowOptions {
  /** Route prefix of `MapMiddtWorkflows` (default `api/workflows`). */
  routePrefix?: string;
}

function createClient(options: MiddtWorkflowOptions): WorkflowApiClient {
  const config = inject(MIDDT_CONFIG);
  return new WorkflowApiClient({ baseUrl: config.apiBaseUrl, fetch: bffFetch(config), credentials: 'same-origin', routePrefix: options.routePrefix });
}

/**
 * Client of the workflow API through the BFF (CSRF header, cookie, login redirect on 401). Available without registration
 * (default route prefix); use {@link provideMiddtWorkflow} to change the prefix.
 */
export const WORKFLOW_API_CLIENT = new InjectionToken<WorkflowApiClient>('WORKFLOW_API_CLIENT', {
  providedIn: 'root',
  factory: () => createClient({}),
});

/** Registers the workflow API client: `provideMiddtWorkflow({ routePrefix: 'api/workflows' })`. */
export function provideMiddtWorkflow(options: MiddtWorkflowOptions = {}): EnvironmentProviders {
  return makeEnvironmentProviders([{ provide: WORKFLOW_API_CLIENT, useFactory: () => createClient(options) }]);
}
