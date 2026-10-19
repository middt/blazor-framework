// Runs the shared conformance suite (spec/expressions.conformance.json) with the same rules as the C# test
// tests/Middt.Framework.Tests/Expressions/PortableExpressionConformanceTests.cs.

import { readFileSync } from 'node:fs';
import { test } from 'node:test';
import assert from 'node:assert/strict';
import { PortableExpression } from '../src/index.ts';
import type { ExpressionContext } from '../src/index.ts';

interface Case {
  expr?: string;
  template?: string;
  truthy?: string;
  expected?: unknown;
  error?: boolean;
}

interface Suite {
  variables: string[];
  context: Record<string, unknown> & { user: { id: string; name: string; roles: string[] }; now: string };
  cases: Case[];
}

const suiteUrl = new URL('../../../../spec/expressions.conformance.json', import.meta.url);
const suiteJson = readFileSync(suiteUrl, 'utf8');
const suite = JSON.parse(suiteJson) as Suite;

/** Parsed per case, so a case cannot change the context of another one. */
function context(): ExpressionContext {
  const source = (JSON.parse(suiteJson) as Suite).context;
  const variables: Record<string, unknown> = {};
  for (const [name, value] of Object.entries(source)) {
    if (name !== 'user' && name !== 'now') {
      variables[name] = value;
    }
  }

  return { variables, user: source.user, now: source.now };
}

/** Numbers compare by value (15000 == 15000.0); everything else by canonical JSON text (sorted keys). */
function normalize(value: unknown): string {
  if (value === null || value === undefined) {
    return 'null';
  }

  if (typeof value === 'number') {
    return String(value === 0 ? 0 : value);
  }

  if (Array.isArray(value)) {
    return `[${value.map(normalize).join(',')}]`;
  }

  if (typeof value === 'object') {
    const entries = Object.entries(value as Record<string, unknown>).sort(([a], [b]) => (a < b ? -1 : a > b ? 1 : 0));
    return `{${entries.map(([k, v]) => `${k}:${normalize(v)}`).join(',')}}`;
  }

  return JSON.stringify(value);
}

suite.cases.forEach((c, index) => {
  const text = c.expr ?? c.template ?? c.truthy ?? '';
  const isTemplate = c.template !== undefined;

  test(`case ${index}: ${JSON.stringify(text)}`, () => {
    const engine = new PortableExpression(suite.variables);
    if (c.error === true) {
      const errors = isTemplate ? engine.validateTemplate(text) : [engine.validate(text)];
      assert.equal(errors.length, 1, `'${text}' must produce exactly one error`);
      assert.notEqual(errors[0], null, `'${text}' must be rejected`);
      return;
    }

    const errors = (isTemplate ? engine.validateTemplate(text) : [engine.validate(text)]).filter(e => e !== null);
    assert.deepEqual(errors, []);
    const actual = isTemplate
      ? engine.render(text, context())
      : c.truthy !== undefined
        ? engine.evaluateCondition(text, context())
        : engine.evaluateToValue(text, context());

    assert.equal(normalize(actual), normalize(c.expected), `'${text}'`);
  });
});

test('suite is not empty', () => {
  assert.ok(suite.cases.length > 100);
});
