import { describe, it, expect, beforeEach } from 'vitest';
import { TestBed } from '@angular/core/testing';
import { provideZonelessChangeDetection } from '@angular/core';
import type { WorkflowData } from '@middt/ui-core';
import { UiLookupCache } from '../ui/lookup-cache.service';
import { MiddtJsonSchemaFormComponent } from './json-schema-form.component';

const schema: WorkflowData = {
  type: 'object',
  required: ['customerId'],
  properties: {
    customerId: { type: 'integer', title: 'Müşteri', 'x-lookup': { entity: 'Customer', valueField: 'customerId', textField: 'email' } },
    storeCode: { type: 'string', title: 'Mağaza', 'x-lookup': { entity: 'Store', valueField: 'code', textField: 'name' } },
    note: { type: 'string', title: 'Not' },
  },
};

const rows: Record<string, unknown[]> = {
  Customer: [{ customerId: 1, email: 'a@test.com' }, { customerId: 2, email: 'b@test.com' }],
  Store: [{ code: 'IST', name: 'İstanbul' }],
};

class FakeLookupCache {
  readonly requested: string[] = [];
  failing = new Set<string>();

  getAll(entity: string): Promise<unknown[]> {
    this.requested.push(entity);
    return this.failing.has(entity) ? Promise.reject(new Error('Sunucu hatası')) : Promise.resolve(rows[entity] ?? []);
  }
}

/** Lets the lookup promises resolve. */
const settle = async (): Promise<void> => {
  for (let i = 0; i < 3; i++) {
    await new Promise(resolve => setTimeout(resolve));
  }
};

describe('MiddtJsonSchemaFormComponent x-lookup', () => {
  let cache: FakeLookupCache;

  beforeEach(() => {
    cache = new FakeLookupCache();
    TestBed.configureTestingModule({
      providers: [provideZonelessChangeDetection(), { provide: UiLookupCache, useValue: cache }],
    });
  });

  async function create(value: WorkflowData | null = null) {
    const fixture = TestBed.createComponent(MiddtJsonSchemaFormComponent);
    fixture.componentRef.setInput('schema', schema);
    fixture.componentRef.setInput('value', value);
    fixture.componentRef.setInput('idPrefix', 'wf-start');
    await fixture.whenStable();
    await settle();
    await fixture.whenStable();
    return fixture;
  }

  it('renders lookup properties as selects with data-lookup and the usual ids', async () => {
    const fixture = await create();
    const host = fixture.nativeElement as HTMLElement;

    const customer = host.querySelector('[data-field="customerId"]')!;
    expect(customer.getAttribute('data-lookup')).toBe('Customer');
    expect(customer.querySelector('mat-select#wf-start-customerId')).not.toBeNull();
    expect(host.querySelector('[data-field="storeCode"]')!.getAttribute('data-lookup')).toBe('Store');
    expect(host.querySelector('[data-field="note"]')!.hasAttribute('data-lookup')).toBe(false);
    expect(host.querySelector('input#wf-start-note')).not.toBeNull();
    expect(cache.requested).toContain('Customer');
    expect(cache.requested).toContain('Store');
  });

  it('loads the options and returns values typed by the property type', async () => {
    const fixture = await create({ customerId: 2, storeCode: 'IST' });
    const form = fixture.componentInstance;
    const options = (form as unknown as { lookupOptions: () => Record<string, { value: string; text: string }[]> }).lookupOptions();

    expect(options['customerId']).toEqual([{ value: '1', text: 'a@test.com' }, { value: '2', text: 'b@test.com' }]);
    expect(options['storeCode']).toEqual([{ value: 'IST', text: 'İstanbul' }]);
    expect(form.validate()).toBe(true);
    expect(form.getValue()).toEqual({ customerId: 2, storeCode: 'IST' });
    expect((fixture.nativeElement as HTMLElement).querySelector('#wf-start-customerId')!.textContent).toContain('b@test.com');
  });

  it('validates required lookup fields like other fields', async () => {
    const fixture = await create();
    expect(fixture.componentInstance.validate()).toBe(false);
    await fixture.whenStable();

    const error = (fixture.nativeElement as HTMLElement).querySelector('#wf-start-customerId-error');
    expect(error?.textContent).toContain('Bu alan zorunludur.');
  });

  it('shows loading errors below the select', async () => {
    cache.failing.add('Store');
    const fixture = await create();
    const field = (fixture.nativeElement as HTMLElement).querySelector('[data-field="storeCode"]')!;

    expect(field.querySelector('.middt-jsf-error.middt-jsf-lookup-error[role="alert"]')?.textContent?.trim()).toBe('Liste yüklenemedi.');
    expect((fixture.nativeElement as HTMLElement).querySelector('[data-field="customerId"] .middt-jsf-lookup-error')).toBeNull();
  });
});
