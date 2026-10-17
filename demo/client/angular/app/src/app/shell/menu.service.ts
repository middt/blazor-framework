import { computed, inject, Injectable, signal } from '@angular/core';
import { AuthService, UI_API_CLIENT } from '@middt/angular/core';
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

/** The demo workflows, in process order (store → staff → catalog → stock → customers → orders). */
export const WORKFLOW_MENU: MenuGroup = {
  name: 'İş Akışları',
  icon: 'account_tree',
  items: [
    { name: 'Mağaza açılışı', path: '/workflow/store-opening', icon: 'storefront' },
    { name: 'Personel alımı', path: '/workflow/staff-onboarding', icon: 'badge' },
    { name: 'Kategori talebi', path: '/workflow/category-request', icon: 'category' },
    { name: 'Ürün lansmanı', path: '/workflow/product-launch', icon: 'new_releases' },
    { name: 'Stok ikmali', path: '/workflow/stock-replenishment', icon: 'inventory_2' },
    { name: 'Müşteri kaydı', path: '/workflow/customer-onboarding', icon: 'person_add' },
    { name: 'Sipariş Onayı', path: '/workflow/order-approval', icon: 'shopping_cart' },
    { name: 'Sipariş karşılama', path: '/workflow/order-fulfillment', icon: 'local_shipping' },
  ],
};

/** Test workflows covering all framework workflow tasks. */
export const TEST_WORKFLOW_MENU: MenuGroup = {
  name: 'Test Akışları',
  icon: 'science',
  items: [
    { name: 'Temel Görevler Testi', path: '/workflow/test-core-tasks', icon: 'play_arrow' },
    { name: 'Entity / DB Testi', path: '/workflow/test-entity-tasks', icon: 'storage' },
    { name: 'Dış Servis / HTTP Testi', path: '/workflow/test-http-tasks', icon: 'http' },
    { name: 'Mesajlaşma Testi', path: '/workflow/test-messaging-tasks', icon: 'send' },
    { name: 'Alt Akış (SubFlow) Testi', path: '/workflow/test-subflow', icon: 'call_split' },
    { name: 'Yeniden Deneme Testi', path: '/workflow/test-retry-tasks', icon: 'replay' },
  ],
};

/** Side menu: the workflow group, fixed entries plus every JSON layout page with a `menu` entry (same as the Blazor sample's MenuService). */
@Injectable({ providedIn: 'root' })
export class MenuService {
  readonly #ui = inject(UI_API_CLIENT);
  readonly #auth = inject(AuthService);
  readonly #layoutItems = signal<MenuItem[]>([]);

  readonly layoutItems = this.#layoutItems.asReadonly();
  readonly workflowGroup = WORKFLOW_MENU;
  readonly testWorkflowGroup = TEST_WORKFLOW_MENU;

  /** Recomputed only when the user (roles) changes, not on every change detection pass. */
  readonly fixedItems = computed<MenuItem[]>(() => [
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
