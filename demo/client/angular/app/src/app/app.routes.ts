import { Routes } from '@angular/router';
import { middtAuthGuard, middtRoleGuard } from '@middt/angular';
import { HomePage } from './pages/home.page';
import { ShellComponent } from './shell/shell.component';

// The home page is eager (first paint); the layout page, the layout editor and the workflow screens are lazy chunks.
// Guards stay eager so a denied route never downloads its chunk.
export const routes: Routes = [
  {
    path: '',
    component: ShellComponent,
    canActivate: [middtAuthGuard],
    children: [
      { path: '', component: HomePage, title: 'Middt Angular Sample' },
      // Every JSON layout of kind "page" – the same layouts the Blazor frontend renders.
      { path: 'ui/:key', loadComponent: () => import('@middt/angular').then(m => m.MiddtLayoutPageComponent) },
      {
        path: 'ui-admin',
        loadComponent: () => import('./pages/ui-admin.page').then(m => m.UiAdminPage),
        canActivate: [middtRoleGuard('ui-admin')],
        title: 'Ekran Tasarımı',
      },
      { path: 'workflow/order-approval', loadComponent: () => import('./pages/order-approval.page').then(m => m.OrderApprovalPage), title: 'Sipariş Onayı' },
      { path: '**', redirectTo: '' },
    ],
  },
];
