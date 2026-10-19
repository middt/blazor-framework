import { test } from 'node:test';
import assert from 'node:assert/strict';
import { UiColumn, UiField, UiFieldJson, validateFields } from '../src/index.ts';

test('columns: defaults, title fallback, sortProperty PascalCase, non-objects skipped', () => {
  const columns = UiColumn.parseAll([
    { field: 'firstName', title: 'Ad', format: 'N2', width: '120px', sortable: false, template: '{{ row.firstName }}' },
    { field: 'lastName' },
    'ignored',
    null,
  ]);
  assert.equal(columns.length, 2);
  assert.equal(columns[0]!.title, 'Ad');
  assert.equal(columns[0]!.sortable, false);
  assert.equal(columns[0]!.sortProperty, 'FirstName');
  assert.equal(columns[0]!.template, '{{ row.firstName }}');
  assert.equal(columns[1]!.title, 'lastName');
  assert.equal(columns[1]!.sortable, true);
  assert.equal(columns[1]!.format, null);
  assert.equal(new UiColumn().sortProperty, '');
  assert.deepEqual(UiColumn.parseAll(undefined), []);
});

test('fields: parse options, lookup and numbers', () => {
  const field = UiField.parse({
    field: 'status',
    type: 'select',
    required: true,
    min: '1,000.5',
    maxLength: 10.9,
    options: ['A', { value: 2, text: 'İki' }, { value: true }],
    lookup: { entity: 'Category', valueField: 'id', textField: 'name', columns: [{ field: 'name' }], search: [{ field: 'name' }] },
  });
  assert.equal(field.label, 'status');
  assert.equal(field.required, true);
  assert.equal(field.min, 1000.5);
  assert.equal(field.maxLength, 10);
  assert.deepEqual(field.options.map(o => [o.value, o.text, o.key]), [['A', 'A', 'A'], [2, 'İki', '2'], [true, 'true', 'true']]);
  assert.equal(field.lookup?.entity, 'Category');
  assert.equal(field.lookup?.columns[0]!.sortProperty, 'Name');
  assert.equal(field.lookup?.search[0]!.type, 'text');
  assert.equal(UiField.parse({ field: 'x' }).lookup, null);
});

test('toDecimal follows NumberStyles.Number (invariant)', () => {
  assert.equal(UiFieldJson.toDecimal(' -12.5 '), -12.5);
  assert.equal(UiFieldJson.toDecimal('5-'), -5);
  assert.equal(UiFieldJson.toDecimal('1,234'), 1234);
  assert.equal(UiFieldJson.toDecimal('.5'), 0.5);
  assert.equal(UiFieldJson.toDecimal('1e3'), null);
  assert.equal(UiFieldJson.toDecimal('12,5.'), 125);
  assert.equal(UiFieldJson.toDecimal('-5-'), null);
  assert.equal(UiFieldJson.toDecimal('abc'), null);
  assert.equal(UiFieldJson.toDecimal(''), null);
  assert.equal(UiFieldJson.toDecimal(true), null);
});

test('validate: required (not for checkboxes), whitespace counts as empty', () => {
  const required = UiField.parse({ field: 'name', required: true });
  assert.equal(required.validate({}), 'Bu alan zorunludur.');
  assert.equal(required.validate({ name: null }), 'Bu alan zorunludur.');
  assert.equal(required.validate({ name: '  ' }), 'Bu alan zorunludur.');
  assert.equal(required.validate({ name: 'x' }), null);
  assert.equal(UiField.parse({ field: 'ok', type: 'checkbox', required: true }).validate({}), null);
  assert.equal(UiField.parse({ field: 'name' }).validate({}), null);
});

test('validate: numbers, integers, min/max with Turkish decimal separator', () => {
  const amount = UiField.parse({ field: 'amount', type: 'number', min: 1.5, max: 100 });
  assert.equal(amount.validate({ amount: 1 }), 'En az 1,5 olmalıdır.');
  assert.equal(amount.validate({ amount: '101' }), 'En fazla 100 olabilir.');
  assert.equal(amount.validate({ amount: 50 }), null);
  assert.equal(amount.validate({ amount: 'abc' }), null); // not a number: not checked
  assert.equal(amount.validate({ amount: 1 }, 'en-US'), 'En az 1.5 olmalıdır.');
  const count = UiField.parse({ field: 'count', type: 'integer' });
  assert.equal(count.validate({ count: 2.5 }), 'Tam sayı olmalıdır.');
  assert.equal(count.validate({ count: 2 }), null);
});

test('validate: maxLength for text and textarea only', () => {
  assert.equal(UiField.parse({ field: 'n', maxLength: 3 }).validate({ n: 'abcd' }), 'En fazla 3 karakter olabilir.');
  assert.equal(UiField.parse({ field: 'n', type: 'textarea', maxLength: 3 }).validate({ n: 'abc' }), null);
  assert.equal(UiField.parse({ field: 'n', type: 'select', maxLength: 3 }).validate({ n: 'abcd' }), null);
});

test('validateFields collects errors by field', () => {
  const fields = UiField.parseAll([{ field: 'a', required: true }, { field: 'b', type: 'integer' }, { field: 'c' }]);
  assert.deepEqual(validateFields(fields, { b: 1.5 }), { a: 'Bu alan zorunludur.', b: 'Tam sayı olmalıdır.' });
});
