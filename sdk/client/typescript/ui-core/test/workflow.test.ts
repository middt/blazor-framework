import { test } from 'node:test';
import assert from 'node:assert/strict';
import {
  formatWorkflowDate,
  isSuccess,
  parseWorkflowDate,
  ResultEnum,
  schemaFields,
  schemaFormValue,
  schemaHasInput,
  schemaInitialValues,
  UNAVAILABLE_MESSAGE,
  validateSchemaField,
  validateSchemaFields,
  WORKFLOW_STATUS_OPTIONS,
  WorkflowApiClient,
  workflowDataGet,
  workflowHistoryPointStyle,
  workflowIsPending,
  workflowLayoutVariables,
  workflowStateTitle,
  workflowStatusBadge,
  workflowStatusText,
  workflowStepClass,
  workflowStepPrefill,
  workflowStepStatusText,
  workflowStepTransition,
  workflowSubTypeText,
  workflowTransitionIcon,
  workflowTransitionStyle,
  workflowTransitionTitle,
  workflowTriggerText,
  workflowViewDate,
  workflowViewNumber,
  workflowViewText,
  workflowWizardSteps,
} from '../src/index.ts';
import type { WorkflowDefinitionModel, WorkflowHistoryModel, WorkflowInstanceModel } from '../src/index.ts';

interface Call {
  url: string;
  init: RequestInit;
}

function fakeFetch(...responses: (Response | Error)[]) {
  const calls: Call[] = [];
  const fetch = async (url: string, init: RequestInit = {}): Promise<Response> => {
    calls.push({ url, init });
    const next = responses.shift();
    if (next === undefined) {
      throw new Error('unexpected request');
    }

    if (next instanceof Error) {
      throw next;
    }

    return next;
  };
  return { fetch, calls };
}

const json = (body: unknown, status = 200, type = 'application/json'): Response =>
  new Response(JSON.stringify(body), { status, headers: { 'Content-Type': type } });
const ok = (data: unknown, count = 1, status = 200): Response => json({ result: 0, messageList: [], count, data }, status);

const ID = '6f1c1e0a-3d5b-4c1e-9a77-0b5b1c2d3e4f';

function instance(overrides: Partial<WorkflowInstanceModel> = {}): WorkflowInstanceModel {
  return {
    id: ID,
    key: 'K-1',
    workflow: 'order-approval',
    workflowVersion: 2,
    state: 'customer',
    stateTitle: 'Müşteri bilgileri',
    stateType: 'Normal',
    stateSubType: 'Human',
    view: null,
    layout: null,
    status: 'Active',
    data: { customerName: 'Acme', company: { taxNumber: '1234567890', taxOffice: 'Kadıköy', extra: 1 } },
    dataVersion: 1,
    startedBy: 'alice',
    createdAt: '2026-01-02T03:04:05',
    updatedAt: '2026-01-02T03:04:05Z',
    availableTransitions: [],
    scheduledTransitions: [],
    ...overrides,
  };
}

const definition: WorkflowDefinitionModel = {
  key: 'order-approval',
  version: 2,
  title: 'Sipariş onayı',
  startState: 'customer',
  states: [
    { key: 'customer', title: 'Müşteri bilgileri', type: 'Normal', subType: 'Human', step: 1 },
    { key: 'customer-routing', title: 'Müşteri tipi kontrolü', type: 'Normal', subType: 'None' },
    { key: 'company', title: 'Firma bilgileri', type: 'Normal', subType: 'Human', step: 2 },
    { key: 'person', title: 'Kimlik bilgileri', type: 'Normal', subType: 'Human', step: 2 },
    { key: 'order-details', title: 'Sipariş detayları', type: 'Normal', subType: 'Human', step: 3 },
    { key: 'approved', title: 'Onaylandı', type: 'Finish', subType: 'Success', step: 4 },
    { key: 'rejected', title: 'Reddedildi', type: 'Finish', subType: 'Error', step: 4 },
    { key: 'cancelled', title: 'İptal edildi', type: 'Finish', subType: 'Cancelled' },
  ],
  transitions: [
    { key: 'submit-customer', title: 'Devam', from: 'customer', target: 'customer-routing', trigger: 'Manual' },
    { key: 'to-company', from: 'customer-routing', target: 'company', trigger: 'Automatic' },
    { key: 'cancel', title: 'İptal et', from: null, target: 'cancelled', trigger: 'Manual' },
  ],
};

const history = (...entries: [string | null, string, string][]): WorkflowHistoryModel[] =>
  entries.map(([from, to, at], i) => ({
    transitionKey: `t${i}`,
    fromState: from,
    toState: to,
    trigger: i === 0 ? 'Start' : 'Manual',
    startedAt: at,
    finishedAt: at,
    dataVersion: i + 1,
  }));

test('workflow API routes, methods and bodies', async () => {
  const { fetch, calls } = fakeFetch(
    ok([{ key: 'order-approval', version: 2 }]),
    ok(definition),
    ok(definition),
    ok(instance(), 1, 201),
    ok([instance()], 7),
    ok(instance()),
    ok(instance()),
    ok(instance(), 1, 202),
    ok([]),
    ok([]),
    ok(instance()),
  );
  const client = new WorkflowApiClient({ baseUrl: 'https://api.example.com/', fetch, credentials: 'include' });

  assert.ok(isSuccess(await client.listWorkflows()));
  assert.equal((await client.getDefinition('order-approval')).data?.version, 2);
  await client.getDefinition('order-approval', 1);
  assert.equal((await client.start('order-approval', { key: 'K 1', data: { a: 1 } })).data?.id, ID);
  const page = await client.listInstances('order-approval', { status: 'Active', state: 'customer', page: 2, pageSize: 10 });
  assert.equal(page.count, 7);
  await client.getInstance('order-approval', ID);
  await client.executeTransition('order-approval', ID, 'submit customer', { customerName: 'Acme' });
  await client.executeTransition('order-approval', ID, 'back', null, true);
  await client.getHistory('order-approval', ID);
  await client.getIncidents('order-approval', ID);
  await client.retry('order-approval', ID);

  const base = 'https://api.example.com/api/workflows/order-approval';
  assert.deepEqual(calls.map(c => `${c.init.method} ${c.url}`), [
    'GET https://api.example.com/api/workflows',
    `GET ${base}`,
    `GET ${base}?version=1`,
    `POST ${base}/instances`,
    `GET ${base}/instances?state=customer&status=Active&page=2&pageSize=10`,
    `GET ${base}/instances/${ID}`,
    `POST ${base}/instances/${ID}/transitions/submit%20customer`,
    `POST ${base}/instances/${ID}/transitions/back?async=true`,
    `GET ${base}/instances/${ID}/history`,
    `GET ${base}/instances/${ID}/incidents`,
    `POST ${base}/instances/${ID}/retry`,
  ]);
  assert.deepEqual(JSON.parse(calls[3]!.init.body as string), { key: 'K 1', data: { a: 1 } });
  assert.deepEqual(JSON.parse(calls[6]!.init.body as string), { customerName: 'Acme' });
  assert.equal(calls[7]!.init.body, undefined);
  assert.equal(calls[10]!.init.body, undefined);
  assert.equal(calls[0]!.init.credentials, 'include');
});

test('default paging, route prefix and encoding of the workflow key', async () => {
  const { fetch, calls } = fakeFetch(ok([], 0), ok([], 0));
  await new WorkflowApiClient({ fetch }).listInstances('a/b');
  await new WorkflowApiClient({ fetch, routePrefix: '/wf/' }).listInstances('x', { key: 'K&1' });
  assert.equal(calls[0]!.url, '/api/workflows/a%2Fb/instances?page=1&pageSize=20');
  assert.equal(calls[1]!.url, '/wf/x/instances?key=K%261&page=1&pageSize=20');
});

test('validation problems and network failures become error envelopes', async () => {
  const problem = { title: 'Input is invalid.', status: 400, errors: { input: ['customerName: required', 'email: invalid'] } };
  const { fetch } = fakeFetch(
    json(problem, 400, 'application/problem+json'),
    json({ title: 'Instance is busy.', status: 409 }, 409, 'application/problem+json'),
    new TypeError('network'),
  );
  const client = new WorkflowApiClient({ fetch });

  const invalid = await client.executeTransition('order-approval', ID, 'submit-customer', {});
  assert.equal(invalid.result, ResultEnum.error);
  assert.deepEqual(invalid.messageList, ['customerName: required', 'email: invalid']);
  assert.deepEqual((await client.start('order-approval')).messageList, ['Instance is busy.']);
  assert.deepEqual((await client.getInstance('order-approval', ID)).messageList, [UNAVAILABLE_MESSAGE]);
});

test('renderView returns the server HTML or the problem', async () => {
  const html = '<article class="sv-view"><h3>Sipariş #1</h3></article>';
  const { fetch, calls } = fakeFetch(
    new Response(html, { status: 200, headers: { 'Content-Type': 'text/html; charset=utf-8' } }),
    json({ title: "State 'customer' has no registered view.", status: 404 }, 404, 'application/problem+json'),
    new TypeError('network'),
  );
  const client = new WorkflowApiClient({ fetch });

  const view = await client.renderView('order-approval', ID);
  assert.ok(isSuccess(view));
  assert.equal(view.data, html);
  assert.equal(calls[0]!.url, `/api/workflows/order-approval/instances/${ID}/view`);
  assert.equal(calls[0]!.init.method, 'GET');

  const missing = await client.renderView('order-approval', ID);
  assert.equal(missing.result, ResultEnum.error);
  assert.deepEqual(missing.messageList, ["State 'customer' has no registered view."]);
  assert.deepEqual((await client.renderView('order-approval', ID)).messageList, [UNAVAILABLE_MESSAGE]);
});

test('aborts propagate', async () => {
  const controller = new AbortController();
  const fetch = async (_url: string, init: RequestInit = {}): Promise<Response> => {
    controller.abort();
    const error = new Error('aborted');
    error.name = 'AbortError';
    assert.equal(init.signal, controller.signal);
    throw error;
  };
  await assert.rejects(new WorkflowApiClient({ fetch }).getInstance('w', ID, controller.signal), { name: 'AbortError' });
});

test('status, sub type and trigger texts', () => {
  assert.equal(workflowStatusText('Active'), 'Aktif');
  assert.equal(workflowStatusText('Cancelled'), 'İptal edildi');
  assert.equal(workflowStatusBadge('Faulted'), 'danger');
  assert.equal(workflowStatusBadge('Cancelled'), 'secondary');
  assert.deepEqual(WORKFLOW_STATUS_OPTIONS.map(o => o.text), ['Aktif', 'İşleniyor', 'Tamamlandı', 'Hatalı', 'İptal edildi']);
  assert.equal(workflowSubTypeText('None'), '');
  assert.equal(workflowSubTypeText('Human'), 'Kullanıcı işlemi bekleniyor');
  assert.equal(workflowTriggerText('Scheduled'), 'Zamanlanmış');
  assert.equal(workflowTriggerText('Custom'), 'Custom');
});

test('dates without a zone are UTC', () => {
  assert.equal(parseWorkflowDate('2026-01-02T03:04:05')?.toISOString(), '2026-01-02T03:04:05.000Z');
  assert.equal(parseWorkflowDate('2026-01-02T03:04:05+02:00')?.toISOString(), '2026-01-02T01:04:05.000Z');
  assert.equal(parseWorkflowDate(null), null);
  assert.match(formatWorkflowDate('2026-01-02T03:04:05Z'), /^\d{2}\.\d{2}\.2026 \d{2}:\d{2}:05$/);
  assert.equal(formatWorkflowDate(undefined), '');
});

test('state and transition titles, styles and icons', () => {
  assert.equal(workflowStateTitle(definition, 'company'), 'Firma bilgileri');
  assert.equal(workflowStateTitle(definition, 'unknown'), 'unknown');
  assert.equal(workflowStateTitle(definition, null), 'Başlangıç');
  assert.equal(workflowTransitionTitle(definition, 'cancel'), 'İptal et');
  assert.equal(workflowTransitionTitle(definition, 'to-company'), 'to-company');
  assert.equal(workflowTransitionStyle(definition, { key: 'a', target: 'approved' }), 'success');
  assert.equal(workflowTransitionStyle(definition, { key: 'r', target: 'rejected' }), 'danger');
  assert.equal(workflowTransitionIcon(definition, { key: 'c', target: 'cancelled' }), 'cancel');
  assert.equal(workflowTransitionIcon(null, { key: 'x', target: 'company' }), 'arrow_forward');
  assert.equal(workflowHistoryPointStyle(definition, history([null, 'approved', '2026-01-01T00:00:00Z'])[0]!), 'success');
});

test('wizard steps: current, done and alternative states', () => {
  const steps = workflowWizardSteps(
    definition,
    instance({ state: 'company' }),
    history([null, 'customer', '2026-01-01T00:00:00Z'], ['customer', 'customer-routing', '2026-01-01T00:01:00Z'], ['customer-routing', 'company', '2026-01-01T00:01:01Z']),
  );
  assert.deepEqual(steps, [
    { number: 1, label: 'Müşteri bilgileri', current: false, completed: true },
    { number: 2, label: 'Firma bilgileri', current: true, completed: false },
    { number: 3, label: 'Sipariş detayları', current: false, completed: false },
    { number: 4, label: 'Onaylandı / Reddedildi', current: false, completed: false },
  ]);
  assert.equal(workflowStepClass(steps[0]!), 'middt-wf-step middt-wf-step-done');
  assert.equal(workflowStepClass(steps[1]!), 'middt-wf-step middt-wf-step-current');
  assert.equal(workflowStepClass(steps[2]!), 'middt-wf-step middt-wf-step-upcoming');
  assert.equal(workflowStepStatusText(steps[1]!), 'şu anki adım');
  assert.equal(workflowStepStatusText(steps[2]!), 'sıradaki adım');

  const finished = workflowWizardSteps(definition, instance({ state: 'approved', status: 'Completed' }), history([null, 'customer', '2026-01-01T00:00:00Z'], ['customer', 'approved', '2026-01-01T00:05:00Z']));
  assert.deepEqual(finished.at(-1), { number: 4, label: 'Onaylandı', current: true, completed: true });
  assert.equal(workflowStepStatusText(finished.at(-1)!), 'tamamlandı, şu anki adım');
  assert.equal(finished[1]!.completed, true, 'steps before the current one are done');

  // Not on a step (subflow / routing / cancelled): everything up to the last visited step is done.
  const cancelled = workflowWizardSteps(definition, instance({ state: 'cancelled', status: 'Cancelled' }), history([null, 'customer', '2026-01-01T00:00:00Z'], ['customer', 'person', '2026-01-01T00:01:00Z']));
  assert.deepEqual(cancelled.map(s => [s.completed, s.current]), [[true, false], [true, false], [false, false], [false, false]]);
  assert.equal(cancelled[1]!.label, 'Kimlik bilgileri');

  assert.deepEqual(workflowWizardSteps({ ...definition, states: definition.states.map(s => ({ ...s, step: null })) }, instance(), []), []);
  assert.deepEqual(workflowWizardSteps(null, instance(), []), []);
});

test('step prefill, inline transition and pending state', () => {
  const schema = { type: 'object', properties: { taxNumber: { type: 'string' }, taxOffice: { type: 'string' }, contactPerson: { type: 'string' } } };
  const data = instance().data;
  assert.deepEqual(workflowStepPrefill(data, 'company', schema), { taxNumber: '1234567890', taxOffice: 'Kadıköy' });
  assert.deepEqual(workflowStepPrefill(data, null, { properties: { customerName: {} } }), { customerName: 'Acme' });
  assert.equal(workflowStepPrefill(data, 'person', schema), null);
  assert.equal(workflowStepPrefill(data, 'company', { properties: {} }), null);

  const step = { key: 'submit-company', target: 'order-details', inline: true, inputInto: 'company', inputSchema: schema };
  const back = { key: 'back', title: 'Geri', target: 'customer' };
  assert.equal(workflowStepTransition(instance({ availableTransitions: [back, step] })), step);
  assert.equal(workflowStepTransition(instance({ availableTransitions: [step], waitingForInstanceId: ID })), null);
  assert.equal(workflowStepTransition(instance({ availableTransitions: [step], status: 'Busy' })), null);
  assert.equal(schemaHasInput(schema), true);
  assert.equal(schemaHasInput({ type: 'object', properties: {} }), false);
  assert.equal(schemaHasInput(null), false);

  assert.equal(workflowIsPending(instance({ status: 'Busy' })), true);
  assert.equal(workflowIsPending(instance({ waitingForInstanceId: ID })), true);
  assert.equal(workflowIsPending(instance()), false);
});

test('layout variables and view data helpers', () => {
  const model = instance({ data: { orderId: 7, amount: 25000.5, credit: { score: 42, checkedAt: '2026-03-04T05:06:07Z' }, flag: true } });
  const variables = workflowLayoutVariables(model);
  assert.deepEqual(variables['instance'], { id: ID, key: 'K-1', state: 'customer', stateTitle: 'Müşteri bilgileri', workflow: 'order-approval', status: 'Active', startedBy: 'alice' });
  assert.deepEqual(variables['data'], model.data);
  assert.notEqual(variables['data'], model.data, 'data is a copy');

  assert.equal(workflowViewText(model.data, 'orderId'), '7');
  assert.equal(workflowViewText(model.data, 'credit.missing'), '-');
  assert.equal(workflowViewText(model.data, 'credit.missing', ''), '');
  assert.equal(workflowViewText(model.data, 'credit'), JSON.stringify(model.data['credit']));
  assert.equal(workflowViewNumber(model.data, 'credit.score'), 42);
  assert.equal(workflowViewNumber(model.data, 'flag'), null);
  assert.equal(workflowViewDate(model.data, 'credit.checkedAt')?.toISOString(), '2026-03-04T05:06:07.000Z');
  assert.equal(workflowViewDate(model.data, 'orderId'), null);
  assert.equal(workflowDataGet(model.data, 'credit.score.x'), null);
});

const customerSchema = {
  type: 'object',
  required: ['customerType', 'customerName', 'email', 'orderId', 'birthDate'],
  properties: {
    customerType: { type: 'string', enum: ['Bireysel', 'Kurumsal'], title: 'Müşteri tipi' },
    customerName: { type: 'string', minLength: 2, maxLength: 100, title: 'Müşteri adı / unvanı' },
    email: { type: 'string', format: 'email', maxLength: 200, title: 'E-posta' },
    note: { type: 'string', maxLength: 500, title: 'Not', description: 'Açıklama' },
    orderId: { type: 'integer', minimum: 1, title: 'Sipariş no' },
    amount: { type: 'number', minimum: 1, maximum: 99.5 },
    express: { type: 'boolean', default: true },
    birthDate: { type: 'string', format: 'date-time' },
    day: { type: 'string', format: 'date' },
    taxNumber: { type: 'string', pattern: '^[0-9]{10}$' },
    extra: { type: 'object', properties: { a: { type: 'string' } } },
    tags: { type: ['array', 'null'] },
  },
};

test('schema fields: kinds and metadata', () => {
  const fields = schemaFields(customerSchema);
  assert.deepEqual(fields.map(f => [f.name, f.kind, f.required]), [
    ['customerType', 'enum', true],
    ['customerName', 'text', true],
    ['email', 'text', true],
    ['note', 'longText', false],
    ['orderId', 'integer', true],
    ['amount', 'number', false],
    ['express', 'boolean', false],
    ['birthDate', 'dateTime', true],
    ['day', 'date', false],
    ['taxNumber', 'text', false],
    ['extra', 'json', false],
    ['tags', 'json', false],
  ]);
  assert.equal(fields[0]!.title, 'Müşteri tipi');
  assert.deepEqual(fields[0]!.enumValues, ['Bireysel', 'Kurumsal']);
  assert.equal(fields[3]!.description, 'Açıklama');
  assert.equal(fields[5]!.title, 'amount');
  assert.deepEqual(schemaFields(null), []);
});

test('schema form validation messages (Turkish, like JsonSchemaForm)', () => {
  const fields = schemaFields(customerSchema);
  const byName = Object.fromEntries(fields.map(f => [f.name, f]));
  const values = schemaInitialValues(customerSchema, fields, null);
  assert.equal(values['express'], true, 'schema default');
  assert.deepEqual(validateSchemaFields(fields, values), {
    customerType: 'Bu alan zorunludur.',
    customerName: 'Bu alan zorunludur.',
    email: 'Bu alan zorunludur.',
    orderId: 'Bu alan zorunludur.',
    birthDate: 'Bu alan zorunludur.',
  });

  assert.equal(validateSchemaField(byName['customerName']!, 'A'), 'En az 2 karakter olmalıdır.');
  assert.equal(validateSchemaField(byName['customerName']!, 'x'.repeat(101)), 'En fazla 100 karakter olabilir.');
  assert.equal(validateSchemaField(byName['customerType']!, 'Diğer'), 'Listeden bir değer seçiniz.');
  assert.equal(validateSchemaField(byName['email']!, 'a@b'), 'Geçerli bir e-posta adresi giriniz.');
  assert.equal(validateSchemaField(byName['email']!, 'a@b.co'), null);
  assert.equal(validateSchemaField(byName['taxNumber']!, '12345'), 'Geçersiz biçim.');
  assert.equal(validateSchemaField(byName['taxNumber']!, '1234567890'), null);
  assert.equal(validateSchemaField(byName['orderId']!, '1.5'), 'Tam sayı olmalıdır.');
  assert.equal(validateSchemaField(byName['orderId']!, '0'), 'En az 1 olmalıdır.');
  assert.equal(validateSchemaField(byName['orderId']!, 'abc'), 'Geçerli bir sayı giriniz.');
  assert.equal(validateSchemaField(byName['amount']!, '100'), 'En fazla 99,5 olabilir.');
  assert.equal(validateSchemaField(byName['amount']!, ''), null);
  assert.equal(validateSchemaField(byName['extra']!, '{'), 'Geçerli bir JSON değeri giriniz.');
  assert.equal(validateSchemaField({ ...byName['taxNumber']!, pattern: '([' }, 'x'), null, 'invalid patterns are left to the server');
});

test('schema form values: initial values and request body', () => {
  const fields = schemaFields(customerSchema);
  const initial = schemaInitialValues(customerSchema, fields, {
    customerType: 'Kurumsal',
    orderId: 12,
    express: false,
    day: '2026-05-06',
    birthDate: '1990-01-02T10:20:00Z',
    extra: { a: 'b' },
    customerName: 5,
  });
  assert.equal(initial['customerType'], 'Kurumsal');
  assert.equal(initial['orderId'], '12');
  assert.equal(initial['express'], false);
  assert.equal(initial['day'], '2026-05-06');
  assert.match(initial['birthDate'] as string, /^1990-01-0[12]T\d{2}:20$/);
  assert.equal(initial['extra'], '{\n  "a": "b"\n}');
  assert.equal(initial['customerName'], '5');

  const body = schemaFormValue(fields, { ...initial, amount: '12,5', note: '', email: 'a@b.co' });
  assert.deepEqual(body, {
    customerType: 'Kurumsal',
    customerName: '5',
    email: 'a@b.co',
    orderId: 12,
    amount: 12.5,
    express: false,
    birthDate: '1990-01-02T10:20:00.000Z',
    day: '2026-05-06',
    extra: { a: 'b' },
  });
});

test('workflow client rejects dot segments in workflow keys, instance ids and transition keys', async () => {
  let requests = 0;
  const client = new WorkflowApiClient({ fetch: async () => { requests++; return new Response('{}'); } });
  for (const bad of ['.', '..']) {
    assert.throws(() => client.getDefinition(bad), /not a valid URL path segment/, bad);
    assert.throws(() => client.start(bad), /not a valid URL path segment/, bad);
    assert.throws(() => client.getInstance('orders', bad), /not a valid URL path segment/, bad);
    assert.throws(() => client.getHistory('orders', bad), /not a valid URL path segment/, bad);
    assert.throws(() => client.executeTransition('orders', 'id-1', bad), /not a valid URL path segment/, bad);
  }

  await assert.rejects(client.renderView('..', 'id-1'), /not a valid URL path segment/);

  assert.equal(requests, 0);
});
