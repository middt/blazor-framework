import { test } from 'node:test';
import assert from 'node:assert/strict';
import { LruCache, PortableExpression, UI_VARIABLES, UiFormats, UiRenderContext, UiUrl } from '../src/index.ts';

const user = { id: '7', name: 'test@test.com', roles: ['manager'] };
const ctx = (variables: Record<string, unknown> = {}, locale?: string): UiRenderContext =>
  new UiRenderContext({ user, variables, now: '2026-10-14T13:00:00.000Z', timeZone: 'Europe/Istanbul', ...(locale ? { locale } : {}) });

test('variables are data, query, row, record, instance', () => {
  assert.deepEqual([...UI_VARIABLES], ['data', 'query', 'row', 'record', 'instance']);
});

test('text renders templates, keeps plain text and reports syntax errors', () => {
  const c = ctx({ data: { amount: 15000, name: 'Ayşe' } });
  assert.equal(c.text(null), '');
  assert.equal(c.text('plain {text}'), 'plain {text}');
  assert.equal(c.text('Merhaba {{ data.name }} / {{ user.Name }}'), 'Merhaba Ayşe / test@test.com');
  assert.equal(c.text('{{ data.amount }}', 'N2'), '15.000,00');
  assert.match(c.text('{{ 1 + }}'), /^⚠ /);
});

test('with adds a variable without changing the parent', () => {
  const parent = ctx({ data: { x: 1 } });
  const child = parent.with('row', { firstName: 'Debra' });
  assert.equal(child.text('{{ row.firstName }} {{ data.x }}'), 'Debra 1');
  assert.equal(parent.text('{{ row.firstName ?? "-" }}'), '-');
  assert.equal(child.locale, 'tr-TR');
});

test('format numbers (tr-TR default)', () => {
  const c = ctx();
  assert.equal(c.format(15000), '15000');
  assert.equal(c.format(12.5), '12,5');
  assert.equal(c.format(0.1 + 0.2), '0,30000000000000004');
  assert.equal(c.format(1234567.891, 'N2'), '1.234.567,89');
  assert.equal(c.format(1234567.891, 'F1'), '1234567,9');
  assert.equal(c.format(1.005, 'N2'), '1,01'); // decimal rounding of the shortest text, like .NET
  assert.equal(c.format(-2.5, 'N0'), '-3');
  assert.equal(c.format(-0.001, 'F2'), '0,00');
  assert.equal(c.format(1000, 'N0'), '1.000');
  assert.equal(ctx({}, 'en-US').format(1234.5, 'N2'), '1,234.50');
});

test('format ignores formats that are not portable', () => {
  const c = ctx();
  assert.equal(c.format(12.5, 'C2'), '12,5');
  assert.equal(c.format(12.5, '0.00'), '12,5');
  assert.equal(c.format('2026-10-14', 'yyyy'), '2026-10-14');
  assert.equal(c.format(12.5, ''), '12,5');
});

test('format dates', () => {
  const c = ctx();
  assert.equal(c.format('2026-10-14', 'd'), '14.10.2026');
  assert.equal(c.format('2026-10-14T13:05:00', 'g'), '14.10.2026 13:05'); // no zone: wall clock
  assert.equal(c.format('2026-10-14T10:05:00Z', 't'), '13:05'); // UTC → Europe/Istanbul
  assert.equal(c.format('2026-10-14T13:05:00+03:00', 'g'), '14.10.2026 13:05');
  assert.equal(c.format('2026-10-14', 'D'), '14 Ekim 2026 Çarşamba');
  assert.equal(c.format('not a date', 'd'), 'not a date');
  assert.equal(c.format('2026-02-30', 'd'), '2026-02-30');
  assert.equal(c.format('2026-10-14'), '2026-10-14');
});

test('format booleans, null and objects', () => {
  const c = ctx();
  assert.equal(c.format(true), 'Evet');
  assert.equal(c.format(false), 'Hayır');
  assert.equal(c.format(null), '');
  assert.equal(c.format(undefined), '');
  assert.equal(c.format({ a: [1, 'x'] }), '{"a":[1,"x"]}');
});

test('isVisible evaluates conditions; failures hide the node', () => {
  const c = ctx({ data: { amount: 15000 } });
  assert.equal(c.isVisible(null), true);
  assert.equal(c.isVisible('   '), true);
  assert.equal(c.isVisible({ widget: 'text' }), true);
  assert.equal(c.isVisible({ widget: 'text', visibleWhen: 'data.amount > 5000' }), true);
  assert.equal(c.isVisible('data.amount > 20000'), false);
  assert.equal(c.isVisible('user.IsInRole("manager")'), true);
  assert.equal(c.isVisible('unknown.x'), false);
  assert.equal(c.isVisible('data.'), false);
});

test('idFor', () => {
  const c = ctx();
  assert.equal(c.idFor({ widget: 'grid' }), 'ui-grid');
  assert.equal(c.idFor({ widget: 'grid', id: 'orders' }, 'search'), 'ui-orders-search');
  assert.equal(new UiRenderContext({ user, idPrefix: 'p' }).idFor({ widget: 'x' }), 'p-x');
});

test('render keeps types of single placeholders', () => {
  const c = ctx({ query: { id: '42' }, data: { n: 3 } });
  assert.deepEqual(c.render({ categoryId: '{{ query.id }}', n: '{{ data.n }}', list: ['a{{ data.n }}'], keep: 5 }), {
    categoryId: '42',
    n: 3,
    list: ['a3'],
    keep: 5,
  });
});

test('UiFormats.isValid', () => {
  for (const f of ['N0', 'N6', 'F0', 'F6', 'd', 'D', 'g', 't']) {
    assert.equal(UiFormats.isValid(f), true, f);
  }

  for (const f of ['N7', 'n2', 'C2', 'G', 'yyyy', '', null, undefined, 'N2 ']) {
    assert.equal(UiFormats.isValid(f), false, String(f));
  }
});

test('UiUrl.isSafe / isLocal', () => {
  assert.equal(UiUrl.isSafe('/ui/orders?id=1'), true);
  assert.equal(UiUrl.isSafe('/'), true);
  assert.equal(UiUrl.isSafe('https://example.com/x'), true);
  assert.equal(UiUrl.isSafe('http://example.com'), true);
  assert.equal(UiUrl.isSafe('//evil.com'), false);
  assert.equal(UiUrl.isSafe('/\\evil.com'), false);
  assert.equal(UiUrl.isSafe('javascript:alert(1)'), false);
  assert.equal(UiUrl.isSafe('data:text/html,x'), false);
  assert.equal(UiUrl.isSafe('ftp://x'), false);
  assert.equal(UiUrl.isSafe('relative/path'), false);
  assert.equal(UiUrl.isSafe(''), false);
  assert.equal(UiUrl.isSafe(null), false);
  assert.equal(UiUrl.isLocal('/ui/x'), true);
  assert.equal(UiUrl.isLocal('https://example.com'), false);
  assert.equal(UiUrl.isLocal('//evil.com'), false);
});

// Expression engine details beyond the shared suite.
const engine = new PortableExpression(['data']);
const run = (expr: string, data: unknown = {}): unknown => engine.evaluateToValue(expr, { variables: { data }, user, now: '2026-10-14T13:00:00.000Z' });

test('number text follows ECMAScript Number.prototype.toString', () => {
  assert.equal(PortableExpression.toText(1e15), '1000000000000000');
  assert.equal(PortableExpression.toText(1e21), '1e+21');
  assert.equal(PortableExpression.toText(0.000001), '0.000001');
  assert.equal(PortableExpression.toText(1e-7), '1e-7');
  assert.equal(PortableExpression.toText(1.5e300), '1.5e+300');
  assert.equal(PortableExpression.toText(-0), '0');
  assert.equal(PortableExpression.toText(-12.25), '-12.25');
  assert.equal(PortableExpression.toText(Number.NaN), '');
});

test('invariant casing and .NET whitespace', () => {
  assert.equal(run('"ßıi".ToUpper()'), 'ßıI');
  assert.equal(run('"İI".ToLower()'), 'İi');
  assert.equal(run('"\u0085 x ﻿".Trim()'), 'x ﻿');
});

test('rounding is half away from zero with .NET steps', () => {
  assert.equal(run('Math.Round(0.5)'), 1);
  assert.equal(run('Math.Round(-0.5)'), -1);
  assert.equal(run('Math.Round(2.675, 2)'), 2.68);
  assert.equal(run('Math.Round(1, 16)'), null);
  assert.equal(run('Math.Round(1.5, 1.5)'), null);
});

test('nesting depth is limited to 64', () => {
  assert.equal(engine.validate('('.repeat(62) + '1' + ')'.repeat(62)), null);
  assert.notEqual(engine.validate('('.repeat(64) + '1' + ')'.repeat(64)), null);
  assert.equal(engine.validate('!'.repeat(63) + '1'), null);
  assert.notEqual(engine.validate('!'.repeat(64) + '1'), null);
});

test('prototype members are not reachable', () => {
  assert.equal(run('data.constructor'), null);
  assert.equal(run('data["__proto__"]'), null);
  assert.equal(run('data.name.constructor', { name: 'x' }), null);
});

test('evaluateToValue returns copies and the user as JSON', () => {
  const data = { list: [1, 2] };
  const value = run('data.list', data) as number[];
  value.push(3);
  assert.deepEqual(data.list, [1, 2]);
  assert.deepEqual(run('user'), { id: '7', name: 'test@test.com', roles: ['manager'] });
  assert.equal(run('"x" + user'), 'xtest@test.com');
  assert.equal(run('Math'), null);
});

test('renderText of a single placeholder object is JSON', () => {
  assert.equal(engine.renderText('{{ data }}', { variables: { data: { a: 1 } }, user }), '{"a":1}');
  assert.equal(PortableExpression.isTemplate('{{}}'), false);
  assert.equal(PortableExpression.isTemplate('a {{ b }}'), true);
});

test('UiUrl rejects control characters and whitespace anywhere', () => {
  for (const url of ['/\t/evil.example', '/\n/evil.example', '/\r/evil.example', '/\u0000/x', '/x\u007f', '/ /evil', '/ /evil', '/﻿/evil', ' /x', '/x\t', 'https://example.com/\t', 'java\tscript:alert(1)']) {
    assert.equal(UiUrl.isSafe(url), false, JSON.stringify(url));
    assert.equal(UiUrl.isLocal(url), false, JSON.stringify(url));
  }

  assert.equal(UiUrl.isLocal('/\\evil.example'), false);
  assert.equal(UiUrl.isLocal('/ui/a%20b?x=1'), true);
  // A plain space inside is percent-encoded by browsers (stays on the site) – same rule as .NET UiUrl.
  assert.equal(UiUrl.isLocal('/a b'), true);
  assert.equal(UiUrl.isLocal('/ /evil.example'), true);
});

test('formatters are reused and keep locale, format and time zone apart', () => {
  const NumberFormat = Intl.NumberFormat;
  const DateTimeFormat = Intl.DateTimeFormat;
  let constructed = 0;
  // Count constructions: the formatter cache must create one per locale/format(/time zone).
  (Intl as { NumberFormat: unknown }).NumberFormat = function (...args: ConstructorParameters<typeof NumberFormat>) {
    constructed++;
    return new NumberFormat(...args);
  };
  (Intl as { DateTimeFormat: unknown }).DateTimeFormat = function (...args: ConstructorParameters<typeof DateTimeFormat>) {
    constructed++;
    return new DateTimeFormat(...args);
  };
  try {
    const c = ctx({}, 'de-CH');
    const first = c.format(1234.5, 'N3');
    for (let i = 0; i < 50; i++) {
      assert.equal(c.format(1234.5, 'N3'), first);
      c.format('2026-10-14T10:00:00Z', 'g');
    }

    assert.equal(constructed, 3); // N3, short date, short time
    assert.notEqual(ctx({}, 'de-CH').format(1234.5, 'F3'), first);
    const utc = new UiRenderContext({ user, locale: 'de-CH', timeZone: 'UTC' }).format('2026-10-14T23:30:00Z', 'd');
    const istanbul = ctx({}, 'de-CH').format('2026-10-14T23:30:00Z', 'd');
    assert.notEqual(utc, istanbul);
  } finally {
    Intl.NumberFormat = NumberFormat;
    Intl.DateTimeFormat = DateTimeFormat;
  }
});

test('expression caches are bounded and templates render consistently from cache', () => {
  const evaluator = new PortableExpression(['data']);
  const context = { variables: { data: { n: 2, s: 'x' } }, user: { name: 'u' }, now: '2026-10-14T13:00:00.000Z' };
  for (let i = 0; i < 3; i++) {
    assert.equal(evaluator.render('{{ data.n }}', context), 2);
    assert.equal(evaluator.render('a{{ data.n }}b{{data.s}}c', context), 'a2bxc');
    assert.equal(evaluator.render('{{ data.n }}{{ data.n }}', context), '22');
    assert.equal(evaluator.render('plain', context), 'plain');
  }

  assert.deepEqual(evaluator.validateTemplate(['{{ data.n }}', { a: '{{ ) }}' }]).length, 1);
  for (let i = 0; i < 1500; i++) {
    assert.equal(evaluator.render(`#{{ data.n + ${i} }}`, context), `#${2 + i}`);
  }

  const cache = new LruCache<string, number>(2);
  cache.set('a', 1);
  cache.set('b', 2);
  cache.get('a');
  cache.set('c', 3);
  assert.equal(cache.get('b'), undefined);
  assert.equal(cache.get('a'), 1);
  assert.equal(cache.size, 2);
});

test('a context changed in place is not evaluated with a stale scope', () => {
  const evaluator = new PortableExpression(['data']);
  const context: { variables: Record<string, unknown>; now: string } = { variables: { data: 1 }, now: '2026-10-14T13:00:00.000Z' };
  assert.equal(evaluator.evaluate('data', context), 1);
  context.variables = { data: 2 };
  assert.equal(evaluator.evaluate('data', context), 2);
});
