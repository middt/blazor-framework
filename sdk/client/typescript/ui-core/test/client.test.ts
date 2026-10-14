import { test } from 'node:test';
import assert from 'node:assert/strict';
import { isSuccess, pathSegment, ResultEnum, UiApiClient, UiDataClient, UNAVAILABLE_MESSAGE } from '../src/index.ts';
import type { UiLayoutDocument } from '../src/index.ts';

interface Call {
  url: string;
  init: RequestInit;
}

function fakeFetch(...responses: (Response | (() => Response) | Error)[]) {
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

    return typeof next === 'function' ? next() : next;
  };
  return { fetch, calls };
}

const json = (body: unknown, status = 200, type = 'application/json'): Response =>
  new Response(JSON.stringify(body), { status, headers: { 'Content-Type': type } });
const ok = (data: unknown, count = 1): Response => json({ result: 0, messageList: [], count, data });

test('layout API routes, methods, headers and credentials', async () => {
  const { fetch, calls } = fakeFetch(ok([]), ok({ key: 'a b', body: [] }), ok({ errors: [] }), ok({ key: 'a b', body: [] }), json({ result: 0, messageList: [] }), ok([]), ok([]));
  const client = new UiApiClient({
    baseUrl: 'https://api.example.com/',
    fetch,
    credentials: 'include',
    headers: async () => ({ Authorization: 'Bearer t' }),
  });
  const layout: UiLayoutDocument = { key: 'a b', version: 3, body: [{ widget: 'heading', text: 'Hi' }] };

  assert.ok(isSuccess(await client.list('page')));
  assert.equal((await client.get('a b')).data?.key, 'a b');
  await client.validate(layout);
  await client.save(layout);
  await client.delete('a b', 3);
  await client.revisions('a b');
  await client.widgets();

  assert.deepEqual(calls.map(c => `${c.init.method} ${c.url}`), [
    'GET https://api.example.com/api/ui/layouts?kind=page',
    'GET https://api.example.com/api/ui/layouts/a%20b',
    'POST https://api.example.com/api/ui/layouts/validate',
    'PUT https://api.example.com/api/ui/layouts/a%20b',
    'DELETE https://api.example.com/api/ui/layouts/a%20b?version=3',
    'GET https://api.example.com/api/ui/layouts/a%20b/revisions',
    'GET https://api.example.com/api/ui/widgets',
  ]);
  assert.equal(calls[0]!.init.credentials, 'include');
  assert.equal((calls[0]!.init.headers as Record<string, string>)['Authorization'], 'Bearer t');
  assert.equal(calls[0]!.init.body, undefined);
  assert.deepEqual(JSON.parse(calls[3]!.init.body as string), layout);
  assert.equal((calls[3]!.init.headers as Record<string, string>)['Content-Type'], 'application/json');
});

test('same-origin base URL and custom route prefix', async () => {
  const { fetch, calls } = fakeFetch(ok([]));
  await new UiApiClient({ fetch, routePrefix: '/admin/ui/' }).list();
  assert.equal(calls[0]!.url, '/admin/ui/layouts');
  assert.equal(calls[0]!.init.credentials, 'same-origin');
});

test('schema returns the JSON schema or null', async () => {
  const schema = { $schema: 'https://json-schema.org/draft/2020-12/schema', type: 'object' };
  const { fetch, calls } = fakeFetch(json(schema, 200, 'application/schema+json'), json({}, 500), new TypeError('network'));
  const client = new UiApiClient({ fetch });
  assert.deepEqual(await client.schema(), schema);
  assert.equal(calls[0]!.url, '/api/ui/schema');
  assert.equal(await client.schema(), null);
  assert.equal(await client.schema(), null);
});

test('failures become error envelopes: validation problem, problem detail, status, network, empty body', async () => {
  const { fetch } = fakeFetch(
    json({ title: 'The layout is invalid.', errors: { layout: ['body[0].widget: unknown', 'x'] } }, 400, 'application/problem+json'),
    json({ title: 'Conflict', detail: 'Version 2 is outdated.' }, 409, 'application/problem+json'),
    new Response('oops', { status: 503, statusText: 'Service Unavailable' }),
    new TypeError('fetch failed'),
    new Response('', { status: 200 }),
    new Response('{not json', { status: 200 }),
  );
  const client = new UiApiClient({ fetch });
  const layout: UiLayoutDocument = { key: 'k', body: [] };

  const invalid = await client.save(layout);
  assert.equal(invalid.result, ResultEnum.error);
  assert.deepEqual(invalid.messageList, ['body[0].widget: unknown', 'x']);
  assert.deepEqual((await client.save(layout)).messageList, ['Version 2 is outdated.']);
  assert.deepEqual((await client.get('k')).messageList, ['Service Unavailable']);
  assert.deepEqual((await client.get('k')).messageList, [UNAVAILABLE_MESSAGE]);
  assert.deepEqual((await client.get('k')).messageList, ['Servis boş yanıt döndü.']);
  assert.deepEqual((await client.get('k')).messageList, [UNAVAILABLE_MESSAGE]);
});

test('401 retries once when onUnauthorized returns true', async () => {
  const { fetch, calls } = fakeFetch(json({}, 401), ok([]));
  let token = 'old';
  const client = new UiApiClient({ fetch, headers: () => ({ Authorization: `Bearer ${token}` }), onUnauthorized: () => ((token = 'new'), true) });
  assert.ok(isSuccess(await client.list()));
  assert.deepEqual(calls.map(c => (c.init.headers as Record<string, string>)['Authorization']), ['Bearer old', 'Bearer new']);
});

test('aborted requests reject', async () => {
  const controller = new AbortController();
  controller.abort();
  const error = new DOMException('aborted', 'AbortError');
  const { fetch } = fakeFetch(error);
  await assert.rejects(new UiApiClient({ fetch }).list(null, controller.signal), { name: 'AbortError' });
});

test('data client routes and search request wire format', async () => {
  const { fetch, calls } = fakeFetch(ok([{ id: 1 }], 1), ok([]), ok({ id: 1 }), ok({}), ok({}), ok({}));
  const client = new UiDataClient({ baseUrl: 'https://api', fetch });
  const request = UiDataClient.searchRequest({ categoryId: 7 }, 2, 25, 'Name', true);
  assert.deepEqual(request, { currentPage: 2, requestItemSize: 25, sortProperty: 'Name', sortReverse: true, requestModel: { categoryId: 7 } });
  assert.equal(UiDataClient.searchRequest({}, 1, 10, undefined, false).sortProperty, null);

  const items = await client.getItems('Product', request);
  assert.equal(items.count, 1);
  await client.getAll('Product');
  await client.getById('Product', 'a/1');
  await client.insert('Product', { name: 'x' });
  await client.update('Product', { id: 1 });
  await client.delete('Product', { id: 1 });
  assert.deepEqual(calls.map(c => `${c.init.method} ${c.url}`), [
    'POST https://api/api/v1.0/Product/getitems',
    'GET https://api/api/v1.0/Product/getall',
    'GET https://api/api/v1.0/Product/getbyid?id=a%2F1',
    'POST https://api/api/v1.0/Product/insert',
    'POST https://api/api/v1.0/Product/update',
    'POST https://api/api/v1.0/Product/delete',
  ]);
  assert.deepEqual(JSON.parse(calls[0]!.init.body as string), request);
});

test('data client rejects invalid entity names', () => {
  const client = new UiDataClient({ fetch: fakeFetch().fetch, apiVersion: '2' });
  for (const entity of ['', '1Product', 'Product/../x', 'Pro duct', 'a'.repeat(65)]) {
    assert.throws(() => client.getAll(entity), /Invalid entity name/, entity);
  }
});

test('layout client rejects dot segments and empty keys before building URLs', () => {
  const { fetch, calls } = fakeFetch();
  const client = new UiApiClient({ fetch });
  for (const key of ['', '.', '..']) {
    assert.throws(() => client.get(key), /not a valid URL path segment/, key);
    assert.throws(() => client.revisions(key), /not a valid URL path segment/, key);
    assert.throws(() => client.delete(key, 1), /not a valid URL path segment/, key);
    assert.throws(() => client.save({ key, body: [] } as unknown as UiLayoutDocument), /not a valid URL path segment/, key);
  }

  assert.equal(calls.length, 0);
});

test('pathSegment encodes everything else', () => {
  assert.equal(pathSegment('a/../b'), 'a%2F..%2Fb');
  assert.equal(pathSegment('...'), '...');
  assert.equal(pathSegment('.x'), '.x');
  assert.equal(pathSegment('%2e%2e'), '%252e%252e');
  assert.throws(() => pathSegment('..'));
});
