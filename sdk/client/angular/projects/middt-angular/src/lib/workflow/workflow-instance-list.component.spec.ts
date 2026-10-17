import { describe, it, expect, beforeEach } from 'vitest';
import { TestBed } from '@angular/core/testing';
import { provideZonelessChangeDetection } from '@angular/core';
import type { StartWorkflowRequest, WorkflowDefinitionModel, WorkflowInstanceModel } from '@middt/ui-core';
import { NotifyService } from '@middt/angular/core';
import { UiLookupCache } from '@middt/angular/ui';
import { WORKFLOW_API_CLIENT } from '../../../workflow/src/workflow-client';
import { MiddtWorkflowInstanceListComponent } from '../../../workflow/src/workflow-instance-list.component';
import type { WorkflowStartOpened } from '../../../workflow/src/workflow-instance-list.component';

const definition: WorkflowDefinitionModel = {
  key: 'store-opening',
  version: 1,
  title: 'Mağaza açılışı',
  startState: 'draft',
  startInputSchema: {
    type: 'object',
    properties: {
      name: { type: 'string', title: 'Ad' },
      categoryId: { type: 'integer', title: 'Kategori', 'x-lookup': { entity: 'category', valueField: 'id', textField: 'name' } },
      urgent: { type: 'boolean', title: 'Acil' },
    },
  },
  states: [{ key: 'draft', title: 'Taslak' } as WorkflowDefinitionModel['states'][number]],
  transitions: [],
};

const existing = {
  id: '00000000-0000-0000-0000-000000000005',
  key: 'category-5',
  workflow: 'store-opening',
  workflowVersion: 1,
  state: 'draft',
  stateType: 'Normal',
  stateSubType: 'None',
  status: 'Active',
  data: {},
  dataVersion: 1,
  createdAt: '2026-01-01T00:00:00Z',
  updatedAt: '2026-01-01T00:00:00Z',
  availableTransitions: [],
  scheduledTransitions: [],
} as WorkflowInstanceModel;

class FakeWorkflowClient {
  started = 0;
  requests: StartWorkflowRequest[] = [];
  instances: WorkflowInstanceModel[] = [];
  startResult: WorkflowInstanceModel | null = null;
  startMessages: string[] = [];
  getDefinition() {
    return Promise.resolve({ result: 0, messageList: [], count: 1, data: definition });
  }
  listInstances() {
    return Promise.resolve({ result: 0, messageList: [], count: this.instances.length, data: this.instances });
  }
  start(_workflow: string, request: StartWorkflowRequest) {
    this.started++;
    this.requests.push(request);
    return Promise.resolve(this.startResult === null
      ? { result: -1, messageList: ['x'], count: 0, data: null }
      : { result: 0, messageList: this.startMessages, count: 1, data: this.startResult });
  }
}

const settle = async (): Promise<void> => {
  for (let i = 0; i < 3; i++) {
    await new Promise(resolve => setTimeout(resolve));
  }
};

describe('MiddtWorkflowInstanceListComponent start dialog', () => {
  let client: FakeWorkflowClient;
  let notified: string[][];
  let notify: Record<string, (title: string, detail?: string | null) => void>;

  beforeEach(() => {
    client = new FakeWorkflowClient();
    notified = [];
    const record = (kind: string) => (title: string, detail?: string | null) => void notified.push([kind, title, detail ?? '']);
    notify = { error: record('error'), success: record('success'), info: record('info') };
    TestBed.configureTestingModule({
      providers: [
        provideZonelessChangeDetection(),
        { provide: WORKFLOW_API_CLIENT, useValue: client },
        { provide: NotifyService, useValue: notify },
        { provide: UiLookupCache, useValue: { getAll: () => Promise.resolve([{ id: 4, name: 'Dağ' }, { id: 5, name: 'Yol' }]) } },
      ],
    });
  });

  async function create(inputs: Record<string, unknown>) {
    const fixture = TestBed.createComponent(MiddtWorkflowInstanceListComponent);
    fixture.componentRef.setInput('workflow', 'store-opening');
    for (const [name, value] of Object.entries(inputs)) {
      fixture.componentRef.setInput(name, value);
    }

    let opened = 0;
    const openedWith: WorkflowStartOpened[] = [];
    const selected: WorkflowInstanceModel[] = [];
    const loaded: string[] = [];
    fixture.componentInstance.startOpened.subscribe(e => {
      opened++;
      openedWith.push(e);
    });
    fixture.componentInstance.selected.subscribe(i => selected.push(i));
    fixture.componentInstance.definitionLoaded.subscribe(d => loaded.push(d.title ?? ''));
    await fixture.whenStable();
    await settle();
    await fixture.whenStable();
    const element = fixture.nativeElement as HTMLElement;
    return {
      fixture, loaded, openedWith, selected, element,
      opened: () => opened,
      dialog: () => element.querySelector('#wf-start-dialog'),
      keyInput: () => element.querySelector<HTMLInputElement>('#wf-start-key'),
    };
  }

  it('opens the start dialog once when openStart is set', async () => {
    const { fixture, loaded, opened, dialog } = await create({ openStart: true });
    expect(loaded).toEqual(['Mağaza açılışı']);
    expect(dialog()).not.toBeNull();
    expect(opened()).toBe(1);
    expect(client.started).toBe(0);
    expect((fixture.nativeElement as HTMLElement).querySelector('[data-field="name"]')).not.toBeNull();
  });

  it('does not open the dialog when starting is not allowed', async () => {
    const { opened, dialog } = await create({ openStart: true, allowStart: false });
    expect(dialog()).toBeNull();
    expect(opened()).toBe(0);
  });

  it('stays closed without openStart and opens with showStartDialog()', async () => {
    const { fixture, dialog } = await create({});
    expect(dialog()).toBeNull();

    fixture.componentInstance.showStartDialog();
    await fixture.whenStable();
    expect(dialog()).not.toBeNull();
  });

  it('prefills the business key and the typed start values when openStart opens the dialog', async () => {
    const { fixture, openedWith, element, keyInput } = await create({
      openStart: true,
      startKey: ' category-5 ',
      startValues: { categoryId: '5', urgent: 'true', unknown: 'x', name: '' },
    });
    await settle();
    await fixture.whenStable();

    expect(keyInput()?.value).toBe('category-5');
    // every start field in the query is reported for the URL cleanup (also the empty 'name'), unknown ones are not
    expect(openedWith).toEqual([{ key: 'category-5', names: ['categoryId', 'urgent', 'name'] }]);
    expect(element.querySelector('[data-field="categoryId"] .mat-mdc-select-value')?.textContent?.trim()).toBe('Yol');
    expect(element.querySelector<HTMLInputElement>('[data-field="urgent"] input[type="checkbox"]')?.checked).toBe(true);
    expect(client.started).toBe(0);
  });

  it('does not prefill when the user opens the dialog', async () => {
    const { fixture, keyInput, element } = await create({ startKey: 'category-5', startValues: { categoryId: '5' } });
    fixture.componentInstance.showStartDialog();
    await fixture.whenStable();

    expect(keyInput()?.value).toBe('');
    expect(element.querySelector('[data-field="categoryId"] .mat-mdc-select-value')?.textContent?.trim()).toBe('Seçiniz');
  });

  it('selects the existing instance returned for the business key without a duplicate row', async () => {
    client.instances = [existing];
    client.startResult = existing;
    client.startMessages = ['Bu iş anahtarıyla açık bir akış zaten var; mevcut akış açıldı.'];
    const { fixture, element, selected } = await create({ openStart: true, startKey: 'category-5', startValues: { categoryId: '5' } });
    await settle();
    await fixture.whenStable();

    element.querySelector<HTMLButtonElement>('.middt-wf-start-submit')?.click();
    await settle();
    await fixture.whenStable();

    expect(client.requests).toEqual([{ key: 'category-5', data: { categoryId: 5, urgent: false } }]);
    expect(selected.map(i => i.id)).toEqual([existing.id]);
    const rows = element.querySelectorAll('.middt-wf-row');
    expect(rows.length).toBe(1);
    expect(rows[0]?.getAttribute('aria-selected')).toBe('true');
    expect(element.querySelector('#wf-start-dialog')).toBeNull();
    // the server's message for an existing instance replaces "Akış başlatıldı"
    expect(notified).toEqual([['info', 'Mevcut akış açıldı', 'Bu iş anahtarıyla açık bir akış zaten var; mevcut akış açıldı.']]);
  });
});
