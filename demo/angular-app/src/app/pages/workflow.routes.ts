import type { Routes } from '@angular/router';

/** Workflow pages are loaded on demand; all state views and forms are backend-driven JSON layouts/schemas. */
export const workflowRoutes: Routes = [
  {
    path: '',
    loadComponent: () => import('./workflow.page').then(m => m.WorkflowPage),
  },
];
