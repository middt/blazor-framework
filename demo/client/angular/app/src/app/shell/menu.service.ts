import { computed, inject, Injectable, signal } from '@angular/core';
import { AuthService, UI_API_CLIENT } from '@middt/angular';
import { isSuccess } from '@middt/ui-core';

export interface MenuItem {
  name: string;
  path: string;
  icon: string;
}

/** Side menu: fixed entries plus every JSON layout page with a `menu` entry (same as the Blazor sample's MenuService). */
@Injectable({ providedIn: 'root' })
export class MenuService {
  readonly #ui = inject(UI_API_CLIENT);
  readonly #auth = inject(AuthService);
  readonly #layoutItems = signal<MenuItem[]>([]);

  readonly layoutItems = this.#layoutItems.asReadonly();

  /** Recomputed only when the user (roles) changes, not on every change detection pass. */
  readonly fixedItems = computed<MenuItem[]>(() => [
    { name: 'Sipariş Onayı', path: '/workflow/order-approval', icon: 'account_tree' },
    ...(this.#auth.hasRole('ui-admin') ? [{ name: 'Ekran Tasarımı', path: '/ui-admin', icon: 'dashboard_customize' }] : []),
  ]);

  async refresh(): Promise<void> {
    const response = await this.#ui.list('page');
    if (isSuccess(response)) {
      this.#layoutItems.set((response.data ?? [])
        .filter(layout => layout.menu)
        .map(layout => ({ name: layout.menu?.title ?? layout.title ?? layout.key, path: `/ui/${layout.key}`, icon: layout.menu?.icon ?? 'article' })));
    }
  }
}
