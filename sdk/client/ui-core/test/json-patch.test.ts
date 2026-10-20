// Runs the diff cases of the shared JSON Patch suite (spec/json-patch.conformance.json) with the same rules as the C# test
// tests/Middt.Framework.Tests/Expressions/JsonPatchConformanceTests.cs.

import { readFileSync } from 'node:fs';
import { test } from 'node:test';
import assert from 'node:assert/strict';
import { jsonPatchDiff, workflowTransitionInput, workflowTransitionPrefill } from '../src/index.ts';
import type { AvailableTransitionModel, WorkflowData } from '../src/index.ts';

interface DiffCase {
  name: string;
  from: WorkflowData;
  to: WorkflowData;
  patch: unknown[];
}

const suite = JSON.parse(readFileSync(new URL('../../../../spec/json-patch.conformance.json', import.meta.url), 'utf8')) as { diff: DiffCase[] };

for (const c of suite.diff) {
  test(`jsonPatchDiff: ${c.name}`, () => {
    assert.deepEqual(jsonPatchDiff(c.from, c.to), c.patch);
  });
}

const contactTransition: AvailableTransitionModel = {
  key: 'update-contact',
  target: '$self',
  inputInto: 'contact',
  inputFormat: 'jsonPatch',
  inputSchema: { type: 'object', properties: { phone: { type: 'string' }, fax: { type: 'string' } } },
};
const data: WorkflowData = { contact: { phone: '1', fax: '2', internal: 'x' }, other: true };

test('a jsonPatch transition form starts from the data it edits (schema properties only)', () => {
  assert.deepEqual(workflowTransitionPrefill(data, contactTransition), { phone: '1', fax: '2' });
  assert.equal(workflowTransitionPrefill(data, { ...contactTransition, inputFormat: null }), null);
});

test('a jsonPatch transition sends the difference; a cleared field is removed', () => {
  assert.deepEqual(workflowTransitionInput(data, contactTransition, { phone: '555', fax: null }), {
    patch: [
      { op: 'replace', path: '/phone', value: '555' },
      { op: 'remove', path: '/fax' },
    ],
  });
  assert.deepEqual(workflowTransitionInput(data, { ...contactTransition, inputFormat: null }, { phone: '555' }), { phone: '555' });
});
