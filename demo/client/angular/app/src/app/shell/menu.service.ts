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

/** The 5 demo workflows showcasing JSON document persistence and all workflow engine capabilities. */
export const WORKFLOW_MENU: MenuGroup = {
  name: 'İş Akışları',
  icon: 'account_tree',
  items: [
    { name: 'Müşteri Kaydı & KYC', path: '/workflow/customer-onboarding', icon: 'person_add' },
    { name: 'Kredi Tahsis & 4-Göz Onay', path: '/workflow/credit-allocation', icon: 'account_balance' },
    { name: 'Sipariş & Ödeme Orkestrasyonu', path: '/workflow/order-payment-orchestration', icon: 'shopping_cart' },
    { name: 'SLA & Eskalasyon Yönetimi', path: '/workflow/sla-escalation', icon: 'timer' },
    { name: 'Risk & Limit Servisi (SubFlow)', path: '/workflow/risk-limit-eval', icon: 'verified_user' },
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
