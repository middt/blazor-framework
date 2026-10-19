import { computed, inject, Injectable, signal } from '@angular/core';
import { AuthService, UI_API_CLIENT } from '@middt/angular/core';
import { WORKFLOW_API_CLIENT } from '@middt/angular/workflow';
import { isSuccess } from '@middt/ui-core';

export interface MenuItem {
  name: string;
  path: string;
  icon: string;
}

/** Titled group of menu entries (rendered as a sub header followed by its entries). */
export interface MenuGroup {
  name: string;
  icon: string;
  items: readonly MenuItem[];
}

/** Side menu: the workflow group (loaded dynamically from `GET /api/workflows`), fixed entries plus every JSON layout page with a `menu` entry. */
@Injectable({ providedIn: 'root' })
export class MenuService {
  readonly #ui = inject(UI_API_CLIENT);
  readonly #workflows = inject(WORKFLOW_API_CLIENT);
  readonly #auth = inject(AuthService);
  readonly #layoutItems = signal<MenuItem[]>([]);
  readonly #workflowItems = signal<readonly MenuItem[]>([]);

  readonly layoutItems = this.#layoutItems.asReadonly();
  readonly workflowGroup = computed<MenuGroup>(() => ({
    name: 'İş Akışları',
    icon: 'account_tree',
    items: this.#workflowItems(),
  }));

  /** Recomputed only when the user (roles) changes, not on every change detection pass. */
  readonly fixedItems = computed<MenuItem[]>(() => [
    ...(this.#auth.hasRole('ui-admin') ? [{ name: 'Ekran Tasarımı', path: '/ui-admin', icon: 'dashboard_customize' }] : []),
  ]);

  async refresh(): Promise<void> {
    const [wfResponse, uiResponse] = await Promise.all([
      this.#workflows.listWorkflows(),
      this.#ui.list('page'),
    ]);

    if (isSuccess(wfResponse) && wfResponse.data && wfResponse.data.length > 0) {
      this.#workflowItems.set(wfResponse.data.map(w => ({
        name: w.title ?? w.key,
        path: `/workflow/${w.key}`,
        icon: w.icon ?? 'account_tree',
      })));
    }

    if (isSuccess(uiResponse)) {
      this.#layoutItems.set((uiResponse.data ?? [])
        .filter(layout => layout.menu)
        .map(layout => ({ name: layout.menu?.title ?? layout.title ?? layout.key, path: `/ui/${layout.key}`, icon: layout.menu?.icon ?? 'article' })));
    }
  }
}

