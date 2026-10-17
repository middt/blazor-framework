import type { Routes } from '@angular/router';
import { provideSampleViews } from '../views/sample-views';

/** Workflow views and the workflow package are loaded only when a workflow route opens. */
export const workflowRoutes: Routes = [
  {
    path: '',
    loadComponent: () => import('./workflow.page').then(m => m.WorkflowPage),
    providers: [provideSampleViews()],
  },
];
