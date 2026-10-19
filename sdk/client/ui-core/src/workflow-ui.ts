// Framework independent display logic of the workflow screens: TypeScript port of WorkflowUi.cs, the wizard/step logic of
// WorkflowInstanceDetail.razor.cs, the JSON Schema form of JsonSchemaForm.razor.cs (Turkish messages) and the data helpers of
// WorkflowViewBase (Middt.Framework.Workflow.Razor/WorkflowViews.cs).

import type {
  AvailableTransitionModel,
  WorkflowData,
  WorkflowDefinitionModel,
  WorkflowHistoryModel,
  WorkflowInstanceModel,
  WorkflowInstanceStatusName,
} from './workflow.ts';
import { WORKFLOW_INSTANCE_STATUSES } from './workflow.ts';

const isObject = (value: unknown): value is WorkflowData => typeof value === 'object' && value !== null && !Array.isArray(value);

const cloneJson = <T>(value: T): T => (value === undefined || value === null ? value : (JSON.parse(JSON.stringify(value)) as T));

// ---------------------------------------------------------------------------------------------------------------- texts

export function workflowStatusText(status: WorkflowInstanceStatusName | string): string {
  switch (status) {
    case 'Active':
      return 'Aktif';
    case 'Busy':
      return 'İşleniyor';
    case 'Completed':
      return 'Tamamlandı';
    case 'Faulted':
      return 'Hatalı';
    case 'Cancelled':
      return 'İptal edildi';
    default:
      return status;
  }
}

export type WorkflowBadgeStyle = 'info' | 'warning' | 'success' | 'danger' | 'secondary';

export function workflowStatusBadge(status: WorkflowInstanceStatusName | string): WorkflowBadgeStyle {
  switch (status) {
    case 'Active':
      return 'info';
    case 'Busy':
      return 'warning';
    case 'Completed':
      return 'success';
    case 'Faulted':
      return 'danger';
    default:
      return 'secondary';
  }
}

/** Status filter options (value + Turkish text). */
export const WORKFLOW_STATUS_OPTIONS: readonly { value: WorkflowInstanceStatusName; text: string }[] = Object.freeze(
  WORKFLOW_INSTANCE_STATUSES.map(value => ({ value, text: workflowStatusText(value) })),
);

export function workflowSubTypeText(subType: string | null | undefined): string {
  switch (subType) {
    case null:
    case undefined:
    case '':
    case 'None':
      return '';
    case 'Human':
      return 'Kullanıcı işlemi bekleniyor';
    case 'Success':
      return 'Başarılı';
    case 'Error':
      return 'Hata / Red';
    case 'Cancelled':
      return 'İptal';
    case 'Timeout':
      return 'Zaman aşımı';
    default:
      return subType;
  }
}

export function workflowTriggerText(trigger: string): string {
  switch (trigger) {
    case 'Manual':
      return 'Kullanıcı';
    case 'Automatic':
      return 'Otomatik';
    case 'Scheduled':
      return 'Zamanlanmış';
    case 'SubFlow':
      return 'Alt akış';
    case 'Start':
      return 'Başlangıç';
    case 'Timeout':
      return 'Zaman aşımı';
    default:
      return trigger;
  }
}

/** Parses a server timestamp; values without a zone designator are UTC (like `DateTimeKind.Unspecified` in WorkflowUi). */
export function parseWorkflowDate(value: string | null | undefined): Date | null {
  if (value === null || value === undefined || value === '') {
    return null;
  }

  const hasZone = /(?:[zZ]|[+-]\d{2}:?\d{2})$/.test(value);
  const date = new Date(hasZone || !value.includes('T') ? value : `${value}Z`);
  return Number.isNaN(date.getTime()) ? null : date;
}

const pad = (value: number, length = 2): string => String(value).padStart(length, '0');

/** `dd.MM.yyyy HH:mm:ss` in the browser's time zone (empty for no value). */
export function formatWorkflowDate(value: string | null | undefined): string {
  const date = parseWorkflowDate(value);
  if (date === null) {
    return value ?? '';
  }

  return `${pad(date.getDate())}.${pad(date.getMonth() + 1)}.${date.getFullYear()} ${pad(date.getHours())}:${pad(date.getMinutes())}:${pad(date.getSeconds())}`;
}

/** Indented JSON (`{}` for no value). */
export function formatWorkflowJson(node: unknown): string {
  return node === null || node === undefined ? '{}' : JSON.stringify(node, null, 2);
}

// ----------------------------------------------------------------------------------------------------------- definition

/** Title of a state of `definition` (`Başlangıç` for no state, the key when unknown). */
export function workflowStateTitle(definition: WorkflowDefinitionModel | null | undefined, state: string | null | undefined): string {
  if (state === null || state === undefined || state === '') {
    return 'Başlangıç';
  }

  return definition?.states.find(s => s.key === state)?.title ?? state;
}

export function workflowTransitionTitle(definition: WorkflowDefinitionModel | null | undefined, key: string): string {
  return definition?.transitions.find(t => t.key === key && t.title !== null && t.title !== undefined && t.title !== '')?.title ?? key;
}

/** `true` when the JSON Schema declares at least one property (a form is needed). */
export function schemaHasInput(schema: WorkflowData | null | undefined): boolean {
  const properties = schema?.['properties'];
  return isObject(properties) && Object.keys(properties).length > 0;
}

export type WorkflowTransitionStyle = 'success' | 'danger' | 'light' | 'primary';

function targetSubType(definition: WorkflowDefinitionModel | null | undefined, target: string): string | undefined {
  return definition?.states.find(s => s.key === target)?.subType;
}

/** Button style of a transition by the sub type of its target state. */
export function workflowTransitionStyle(definition: WorkflowDefinitionModel | null | undefined, transition: AvailableTransitionModel): WorkflowTransitionStyle {
  switch (targetSubType(definition, transition.target)) {
    case 'Success':
      return 'success';
    case 'Error':
      return 'danger';
    case 'Cancelled':
    case 'Timeout':
      return 'light';
    default:
      return 'primary';
  }
}

/** Material icon of a transition by the sub type of its target state. */
export function workflowTransitionIcon(definition: WorkflowDefinitionModel | null | undefined, transition: AvailableTransitionModel): string {
  switch (targetSubType(definition, transition.target)) {
    case 'Success':
      return 'check_circle';
    case 'Error':
      return 'block';
    case 'Cancelled':
    case 'Timeout':
      return 'cancel';
    default:
      return 'arrow_forward';
  }
}

export type WorkflowPointStyle = 'success' | 'danger' | 'secondary' | 'primary';

/** Timeline point style of a history entry by the sub type of the entered state. */
export function workflowHistoryPointStyle(definition: WorkflowDefinitionModel | null | undefined, entry: WorkflowHistoryModel): WorkflowPointStyle {
  switch (targetSubType(definition, entry.toState)) {
    case 'Success':
      return 'success';
    case 'Error':
      return 'danger';
    case 'Cancelled':
    case 'Timeout':
      return 'secondary';
    default:
      return 'primary';
  }
}

// --------------------------------------------------------------------------------------------------------------- wizard

export interface WorkflowWizardStep {
  number: number;
  label: string;
  current: boolean;
  completed: boolean;
}

/** Wizard steps (states grouped by `step`); empty when the definition has none. */
export function workflowWizardSteps(
  definition: WorkflowDefinitionModel | null | undefined,
  instance: WorkflowInstanceModel | null | undefined,
  history: readonly WorkflowHistoryModel[],
): WorkflowWizardStep[] {
  if (!definition || !instance || !definition.states.some(s => s.step !== null && s.step !== undefined)) {
    return [];
  }

  // Most recently entered state per key (history is not guaranteed to be ordered).
  const visited = new Map<string, number>();
  for (const entry of history) {
    const time = parseWorkflowDate(entry.finishedAt)?.getTime() ?? 0;
    visited.set(entry.toState, Math.max(visited.get(entry.toState) ?? Number.NEGATIVE_INFINITY, time));
  }

  const stepStates = definition.states.filter(s => s.step !== null && s.step !== undefined);
  const stepOf = new Map(stepStates.map(s => [s.key, s.step!] as const));
  const current = stepOf.get(instance.state) ?? null;
  const finished = instance.status === 'Completed' || instance.status === 'Cancelled';

  // Not on a step (e.g. a subflow or routing state, cancelled): everything up to the last visited step is done.
  const reached = current ?? Math.max(0, ...[...visited.keys()].filter(k => stepOf.has(k)).map(k => stepOf.get(k)!));

  const groups = new Map<number, typeof stepStates>();
  for (const state of stepStates) {
    const list = groups.get(state.step!) ?? [];
    list.push(state);
    groups.set(state.step!, list);
  }

  return [...groups.entries()]
    .sort((a, b) => a[0] - b[0])
    .map(([number, states]) => {
      const shown =
        states.find(s => s.key === instance.state) ??
        states.filter(s => visited.has(s.key)).sort((a, b) => visited.get(b.key)! - visited.get(a.key)!)[0];
      const label = shown ? (shown.title ?? shown.key) : [...new Set(states.map(s => s.title ?? s.key))].join(' / ');
      const isCurrent = number === current;
      const completed = isCurrent ? finished : number < reached || (current === null && number === reached) || states.some(s => visited.has(s.key));
      return { number, label, current: isCurrent, completed };
    });
}

export function workflowStepClass(step: WorkflowWizardStep): string {
  return (
    'middt-wf-step' +
    (step.current ? ' middt-wf-step-current' : '') +
    (step.completed ? ' middt-wf-step-done' : '') +
    (!step.current && !step.completed ? ' middt-wf-step-upcoming' : '')
  );
}

/** Screen reader status of a step. */
export function workflowStepStatusText(step: WorkflowWizardStep): string {
  if (step.current) {
    return step.completed ? 'tamamlandı, şu anki adım' : 'şu anki adım';
  }

  return step.completed ? 'tamamlandı' : 'sıradaki adım';
}

/** Data path value (`a.b.c`); null when missing. */
export function workflowDataGet(data: WorkflowData | null | undefined, path: string): unknown {
  let current: unknown = data;
  for (const segment of path.split('.').map(s => s.trim()).filter(s => s !== '')) {
    current = isObject(current) && Object.hasOwn(current, segment) ? current[segment] : null;
    if (current === null || current === undefined) {
      return null;
    }
  }

  return current ?? null;
}

/**
 * Initial value of a step form: the data stored at `inputInto` (dot path; root when empty) when the step is revisited,
 * limited to the properties declared by `schema`. null when nothing is stored.
 */
export function workflowStepPrefill(data: WorkflowData | null | undefined, inputInto: string | null | undefined, schema: WorkflowData | null | undefined): WorkflowData | null {
  const properties = schema?.['properties'];
  if (!data || !isObject(properties) || Object.keys(properties).length === 0) {
    return null;
  }

  const source = inputInto === null || inputInto === undefined || inputInto.trim() === '' ? data : workflowDataGet(data, inputInto);
  if (!isObject(source)) {
    return null;
  }

  const result: WorkflowData = {};
  for (const name of Object.keys(properties)) {
    const value = source[name];
    if (value !== null && value !== undefined) {
      result[name] = cloneJson(value);
    }
  }

  return Object.keys(result).length > 0 ? result : null;
}

/** The inline (wizard step) transition of an active instance that does not wait for a subflow. */
export function workflowStepTransition(instance: WorkflowInstanceModel | null | undefined): AvailableTransitionModel | null {
  if (!instance || instance.status !== 'Active' || (instance.waitingForInstanceId ?? null) !== null) {
    return null;
  }

  return instance.availableTransitions.find(t => t.inline === true) ?? null;
}

/** Busy, or active and waiting for a subflow: the UI polls until the next step appears. */
export function workflowIsPending(instance: WorkflowInstanceModel | null | undefined): boolean {
  return !!instance && (instance.status === 'Busy' || (instance.status === 'Active' && (instance.waitingForInstanceId ?? null) !== null));
}

/** Variables of a state's JSON layout: `data` (instance data) and `instance` (id, key, state, workflow, status, …). */
export function workflowLayoutVariables(instance: WorkflowInstanceModel): Record<string, unknown> {
  return {
    data: cloneJson(instance.data ?? {}),
    instance: {
      id: instance.id,
      key: instance.key ?? null,
      state: instance.state,
      stateTitle: instance.stateTitle ?? null,
      workflow: instance.workflow,
      status: instance.status,
      startedBy: instance.startedBy ?? null,
    },
  };
}

// ------------------------------------------------------------------------------------------------------ JSON Schema form

export type SchemaFieldKind = 'text' | 'longText' | 'enum' | 'dateTime' | 'date' | 'number' | 'integer' | 'boolean' | 'json';

/** Editor value of a field: `boolean` for check boxes, text otherwise (`yyyy-MM-dd` / `yyyy-MM-ddTHH:mm` local for dates). */
export type SchemaFieldValue = string | boolean;

/**
 * `x-lookup` of a schema property: the value is picked from the rows of an API entity (`api/v{version}/{entity}/…`), same
 * shape as the layout field `lookup`. The property's `type` decides the value type (integer/number → number, else string).
 */
export interface SchemaLookup {
  entity: string;
  valueField: string;
  textField: string;
}

/** A property of a flat `type: object` JSON Schema. */
export interface SchemaField {
  name: string;
  title: string;
  description: string | null;
  required: boolean;
  kind: SchemaFieldKind;
  minLength: number | null;
  maxLength: number | null;
  minimum: number | null;
  maximum: number | null;
  pattern: string | null;
  format: string | null;
  enumValues: string[];
  /** Options loaded from an entity (`x-lookup`); the field is then shown as a select. */
  lookup: SchemaLookup | null;
}

const LONG_TEXT_THRESHOLD = 200;
const EMAIL = /^[^@\s]+@[^@.\s]+(?:\.[^@.\s]+)+$/;
const MAX_PATTERN_CACHE_SIZE = 256;
const patternCache = new Map<string, RegExp | null>();

function getPatternRegex(pattern: string): RegExp | null {
  if (patternCache.has(pattern)) {
    return patternCache.get(pattern) ?? null;
  }

  let regex: RegExp | null = null;
  try {
    regex = new RegExp(pattern);
  } catch {
    regex = null; // invalid pattern: leave it to the server
  }

  if (patternCache.size >= MAX_PATTERN_CACHE_SIZE) {
    patternCache.clear();
  }

  patternCache.set(pattern, regex);
  return regex;
}

const getString = (obj: WorkflowData, name: string): string | null => (typeof obj[name] === 'string' ? (obj[name] as string) : null);
const getNumber = (obj: WorkflowData, name: string): number | null => (typeof obj[name] === 'number' && Number.isFinite(obj[name]) ? (obj[name] as number) : null);

/** Parses `x-lookup`; null when missing or incomplete (entity, valueField and textField are required). */
export function schemaLookup(property: WorkflowData): SchemaLookup | null {
  const lookup = property['x-lookup'];
  if (!isObject(lookup)) {
    return null;
  }

  const entity = getString(lookup, 'entity');
  const valueField = getString(lookup, 'valueField');
  const textField = getString(lookup, 'textField');
  return entity && valueField && textField ? { entity, valueField, textField } : null;
}

/** Option of a lookup select: the editor value (text, like every schema form value) and the shown text. */
export interface SchemaLookupOption {
  value: string;
  text: string;
}

const optionText = (value: unknown): string =>
  value === null || value === undefined ? '' : typeof value === 'string' ? value : typeof value === 'object' ? (JSON.stringify(value) ?? '') : String(value);

/**
 * Select options of a lookup from the entity rows: value = `row[valueField]` as text (numbers in invariant form), text =
 * `row[textField]` (the value when empty). Rows without a value are skipped, duplicate values keep the first row.
 */
export function schemaLookupOptions(lookup: SchemaLookup, rows: readonly unknown[]): SchemaLookupOption[] {
  const options: SchemaLookupOption[] = [];
  const seen = new Set<string>();
  for (const row of rows) {
    if (!isObject(row)) {
      continue;
    }

    const value = optionText(row[lookup.valueField]);
    if (value === '' || seen.has(value)) {
      continue;
    }

    seen.add(value);
    const text = optionText(row[lookup.textField]);
    options.push({ value, text: text === '' ? value : text });
  }

  return options;
}

function schemaType(property: WorkflowData): string {
  const type = property['type'];
  if (typeof type === 'string') {
    return type;
  }

  if (Array.isArray(type)) {
    return type.find((t): t is string => typeof t === 'string' && t !== 'null') ?? 'string';
  }

  return property['properties'] !== undefined ? 'object' : 'string';
}

/** Fields of a JSON Schema (`type: object` with flat `properties`); nested objects/arrays become JSON text fields. */
export function schemaFields(schema: WorkflowData | null | undefined): SchemaField[] {
  const properties = schema?.['properties'];
  if (!isObject(properties)) {
    return [];
  }

  const requiredList = schema?.['required'];
  const required = new Set(Array.isArray(requiredList) ? requiredList.filter((r): r is string => typeof r === 'string') : []);
  const fields: SchemaField[] = [];
  for (const [name, node] of Object.entries(properties)) {
    if (!isObject(node)) {
      continue;
    }

    const maxLength = getNumber(node, 'maxLength');
    const minLength = getNumber(node, 'minLength');
    const format = getString(node, 'format');
    const type = schemaType(node);
    const values = node['enum'];
    const lookup = schemaLookup(node);
    let kind: SchemaFieldKind;
    let enumValues: string[] = [];
    if (lookup !== null) {
      // Lookup values are plain numbers or texts (the select stores the option value as text).
      kind = type === 'integer' ? 'integer' : type === 'number' ? 'number' : 'text';
    } else if (Array.isArray(values)) {
      kind = 'enum';
      enumValues = values.map(v => (typeof v === 'string' ? v : (JSON.stringify(v) ?? '')));
    } else if (type === 'string') {
      kind = format === 'date-time' ? 'dateTime' : format === 'date' ? 'date' : maxLength !== null && maxLength > LONG_TEXT_THRESHOLD ? 'longText' : 'text';
    } else if (type === 'integer') {
      kind = 'integer';
    } else if (type === 'number') {
      kind = 'number';
    } else if (type === 'boolean') {
      kind = 'boolean';
    } else {
      kind = 'json';
    }

    fields.push({
      name,
      title: getString(node, 'title') ?? name,
      description: getString(node, 'description'),
      required: required.has(name),
      kind,
      minLength: minLength === null ? null : Math.trunc(minLength),
      maxLength: maxLength === null ? null : Math.trunc(maxLength),
      minimum: getNumber(node, 'minimum'),
      maximum: getNumber(node, 'maximum'),
      pattern: getString(node, 'pattern'),
      format,
      enumValues,
      lookup,
    });
  }

  return fields;
}

/** Default value of a field's property (from `schema.properties[name].default`). */
function schemaDefault(schema: WorkflowData | null | undefined, name: string): unknown {
  const properties = schema?.['properties'];
  const property = isObject(properties) ? properties[name] : undefined;
  return isObject(property) ? property['default'] : undefined;
}

const localDate = (date: Date): string => `${date.getFullYear()}-${pad(date.getMonth() + 1)}-${pad(date.getDate())}`;
const localDateTime = (date: Date): string => `${localDate(date)}T${pad(date.getHours())}:${pad(date.getMinutes())}`;

/** Editor value of `node` (initial value or schema default) for `field`; empty when the value does not match the kind. */
export function schemaFieldInitial(field: SchemaField, node: unknown): SchemaFieldValue {
  if (field.kind === 'boolean') {
    return node === true;
  }

  if (node === null || node === undefined) {
    return '';
  }

  switch (field.kind) {
    case 'number':
    case 'integer':
      return typeof node === 'number' && Number.isFinite(node) ? String(node) : '';
    case 'date': {
      if (typeof node !== 'string') {
        return '';
      }

      if (/^\d{4}-\d{2}-\d{2}$/.test(node)) {
        return node;
      }

      const date = parseWorkflowDate(node);
      return date === null ? '' : localDate(date);
    }
    case 'dateTime': {
      const date = typeof node === 'string' ? parseWorkflowDate(node) : null;
      return date === null ? '' : localDateTime(date);
    }
    case 'json':
      return formatWorkflowJson(node);
    default:
      return typeof node === 'string' ? node : JSON.stringify(node);
  }
}

/** Initial editor values: `value[name]`, else the schema `default`. */
export function schemaInitialValues(schema: WorkflowData | null | undefined, fields: readonly SchemaField[], value: WorkflowData | null | undefined): Record<string, SchemaFieldValue> {
  const result: Record<string, SchemaFieldValue> = {};
  for (const field of fields) {
    const node = value?.[field.name] ?? schemaDefault(schema, field.name);
    result[field.name] = schemaFieldInitial(field, node);
  }

  return result;
}

const asText = (value: SchemaFieldValue | undefined): string => (typeof value === 'string' ? value : '');

function parseNumber(text: string): number | null {
  const trimmed = text.trim().replace(',', '.');
  if (trimmed === '' || !/^[+-]?(\d+\.?\d*|\.\d+)([eE][+-]?\d+)?$/.test(trimmed)) {
    return null;
  }

  const number = Number(trimmed);
  return Number.isFinite(number) ? number : null;
}

const formatDecimal = (value: number): string => String(value).replace('.', ',');

/** Validation message of a field (Turkish, like JsonSchemaForm); null when valid. */
export function validateSchemaField(field: SchemaField, value: SchemaFieldValue | undefined): string | null {
  if (field.lookup !== null) {
    // The value comes from the entity rows: only "required" applies (like JsonSchemaForm's lookup fields).
    return field.required && asText(value) === '' ? 'Bu alan zorunludur.' : null;
  }

  switch (field.kind) {
    case 'number':
    case 'integer': {
      const text = asText(value);
      if (text.trim() === '') {
        return field.required ? 'Bu alan zorunludur.' : null;
      }

      const number = parseNumber(text);
      if (number === null) {
        return 'Geçerli bir sayı giriniz.';
      }

      if (field.kind === 'integer' && !Number.isInteger(number)) {
        return 'Tam sayı olmalıdır.';
      }

      if (field.minimum !== null && number < field.minimum) {
        return `En az ${formatDecimal(field.minimum)} olmalıdır.`;
      }

      if (field.maximum !== null && number > field.maximum) {
        return `En fazla ${formatDecimal(field.maximum)} olabilir.`;
      }

      return null;
    }

    case 'boolean':
      return null;

    case 'dateTime':
    case 'date':
      return field.required && asText(value) === '' ? 'Bu alan zorunludur.' : null;

    case 'json': {
      const text = asText(value);
      if (text.trim() === '') {
        return field.required ? 'Bu alan zorunludur.' : null;
      }

      try {
        JSON.parse(text);
        return null;
      } catch {
        return 'Geçerli bir JSON değeri giriniz.';
      }
    }

    default: {
      const text = asText(value);
      if (text === '') {
        return field.required ? 'Bu alan zorunludur.' : null;
      }

      if (field.minLength !== null && text.length < field.minLength) {
        return `En az ${field.minLength} karakter olmalıdır.`;
      }

      if (field.maxLength !== null && text.length > field.maxLength) {
        return `En fazla ${field.maxLength} karakter olabilir.`;
      }

      if (field.kind === 'enum' && !field.enumValues.includes(text)) {
        return 'Listeden bir değer seçiniz.';
      }

      if (field.kind === 'text' && field.format === 'email' && !EMAIL.test(text)) {
        return 'Geçerli bir e-posta adresi giriniz.';
      }

      if (field.pattern !== null && field.pattern !== '') {
        const regex = getPatternRegex(field.pattern);
        if (regex !== null && !regex.test(text)) {
          return 'Geçersiz biçim.';
        }
      }

      return null;
    }
  }
}

/** Validates all fields; returns the messages by field name (empty when valid). */
export function validateSchemaFields(fields: readonly SchemaField[], values: Record<string, SchemaFieldValue>): Record<string, string> {
  const errors: Record<string, string> = {};
  for (const field of fields) {
    const error = validateSchemaField(field, values[field.name]);
    if (error !== null) {
      errors[field.name] = error;
    }
  }

  return errors;
}

/** JSON value of a field; undefined when empty (empty optional fields are omitted). */
export function schemaFieldToJson(field: SchemaField, value: SchemaFieldValue | undefined): unknown {
  const text = asText(value);
  switch (field.kind) {
    case 'boolean':
      return value === true;
    case 'integer':
    case 'number': {
      const number = parseNumber(text);
      return number === null ? undefined : number;
    }
    case 'dateTime': {
      if (text === '') {
        return undefined;
      }

      const date = new Date(text); // local time of the browser
      return Number.isNaN(date.getTime()) ? undefined : date.toISOString();
    }
    case 'date':
      return /^\d{4}-\d{2}-\d{2}$/.test(text) ? text : undefined;
    case 'json':
      if (text.trim() === '') {
        return undefined;
      }

      try {
        return JSON.parse(text) as unknown;
      } catch {
        return undefined;
      }

    default:
      return text === '' ? undefined : text;
  }
}

/** Builds the request body (empty optional fields are omitted). */
export function schemaFormValue(fields: readonly SchemaField[], values: Record<string, SchemaFieldValue>): WorkflowData {
  const result: WorkflowData = {};
  for (const field of fields) {
    const json = schemaFieldToJson(field, values[field.name]);
    if (json !== undefined) {
      result[field.name] = json;
    }
  }

  return result;
}

// ------------------------------------------------------------------------------------------- start dialog prefill (URL)

/**
 * Query parameters of a "start new" link (`/workflow/{key}?start=1&key=…&{property}=…`, same contract as WorkflowPage.razor):
 * `start` (`1`/`true`) opens the start dialog, `key` prefills the business key and every other parameter is a candidate for a
 * start input property (see `schemaValuesFromQuery`).
 */
export interface WorkflowStartQuery {
  open: boolean;
  /** Business key (trimmed); null when missing or empty. */
  key: string | null;
  /** The other parameters (first value of repeated ones). */
  values: Record<string, string>;
}

/** Query parameter names reserved by the start link contract. */
export const WORKFLOW_START_QUERY_PARAM = 'start';
export const WORKFLOW_START_KEY_PARAM = 'key';

/** First text of a query value (`string`, repeated `string[]`, or a number/boolean given programmatically); null otherwise. */
function queryText(value: unknown): string | null {
  if (Array.isArray(value)) {
    return value.length > 0 ? queryText(value[0]) : null;
  }

  if (typeof value === 'string') {
    return value;
  }

  return typeof value === 'number' || typeof value === 'boolean' ? String(value) : null;
}

/** Splits the query of a page into the start link parts (`start`, `key`, the rest). */
export function workflowStartQuery(query: Readonly<Record<string, unknown>> | null | undefined): WorkflowStartQuery {
  const values: Record<string, string> = {};
  let open = false;
  let key: string | null = null;
  for (const [name, value] of Object.entries(query ?? {})) {
    const text = queryText(value);
    if (text === null) {
      continue;
    }

    if (name === WORKFLOW_START_QUERY_PARAM) {
      open = text === '1' || text.toLowerCase() === 'true';
    } else if (name === WORKFLOW_START_KEY_PARAM) {
      key = text.trim() === '' ? null : text.trim();
    } else {
      values[name] = text;
    }
  }

  return { open, key, values };
}

/**
 * Typed value of a query text for a schema property: integer/number → number (invariant, `,` accepted as decimal separator;
 * integers must be whole), boolean → `true`/`1`/`false`/`0`, string (also enums, dates and string `x-lookup`s) → the text.
 * undefined when the text does not fit the type or the type is not a scalar (objects/arrays are not taken from URLs).
 */
export function schemaQueryValue(property: WorkflowData, text: string): string | number | boolean | undefined {
  switch (schemaType(property)) {
    case 'integer': {
      const number = parseNumber(text);
      return number !== null && Number.isInteger(number) ? number : undefined;
    }
    case 'number':
      return parseNumber(text) ?? undefined;
    case 'boolean': {
      const value = text.trim().toLowerCase();
      return value === 'true' || value === '1' ? true : value === 'false' || value === '0' ? false : undefined;
    }
    case 'string':
      return text === '' ? undefined : text;
    default:
      return undefined;
  }
}

/** Start form values taken from query parameters and the names of the parameters that were applied. */
export interface SchemaQueryValues {
  data: WorkflowData;
  names: string[];
}

/**
 * Initial value of a schema form from (URL) parameters: only names declared in `schema.properties` are taken, converted by
 * the property type (`schemaQueryValue`); unknown names and values that do not fit the type are ignored.
 */
export function schemaValuesFromQuery(schema: WorkflowData | null | undefined, values: Readonly<Record<string, unknown>> | null | undefined): SchemaQueryValues {
  const properties = schema?.['properties'];
  const data: WorkflowData = {};
  const names: string[] = [];
  if (!isObject(properties)) {
    return { data, names };
  }

  for (const [name, value] of Object.entries(values ?? {})) {
    const property = Object.hasOwn(properties, name) ? properties[name] : undefined;
    if (!isObject(property)) {
      continue;
    }

    // Every matching start field is reported (also when it is empty or does not fit the type), so the page removes it
    // from the URL – same as Blazor's WorkflowUi.StartPrefillParameters.
    names.push(name);
    const text = queryText(value);
    if (text === null) {
      continue;
    }

    const typed = schemaQueryValue(property, text);
    if (typed !== undefined) {
      data[name] = typed;
    }
  }

  return { data, names };
}

// ------------------------------------------------------------------------------------------- data schema list columns

/** Column/field definition extracted from a workflow's JSON data schema for instance list grids and search bars. */
export interface WorkflowDataColumn {
  key: string;
  title: string;
  type: string;
  searchable?: boolean;
  options?: string[];
}

const trNumberFormat0 = new Intl.NumberFormat('tr-TR', { maximumFractionDigits: 0 });
const trNumberFormat2 = new Intl.NumberFormat('tr-TR', { minimumFractionDigits: 2, maximumFractionDigits: 2 });

/**
 * Extracts columns/fields from `definition.dataSchema` (falling back to `definition.startInputSchema`),
 * excluding complex object/array properties or (when `includeAll` is `false`) properties with `"x-list": false`.
 */
export function workflowDataColumns(definition: WorkflowDefinitionModel | null | undefined, includeAll = false): WorkflowDataColumn[] {
  const schema = definition?.dataSchema ?? definition?.startInputSchema;
  const properties = schema?.['properties'];
  if (!isObject(properties)) {
    return [];
  }

  const columns: WorkflowDataColumn[] = [];
  for (const [key, node] of Object.entries(properties)) {
    if (!isObject(node) || (!includeAll && node['x-list'] === false)) {
      continue;
    }

    const type = schemaType(node);
    if (type === 'object' || type === 'array') {
      continue;
    }

    const title = getString(node, 'title');
    const rawSearch = node['x-search'];
    const searchable = rawSearch === true || (typeof rawSearch === 'string' && rawSearch.trim() !== '');
    const rawEnum = node['enum'];
    const options = Array.isArray(rawEnum)
      ? rawEnum.map(e => (typeof e === 'string' ? e : e !== null && e !== undefined ? String(e) : '')).filter(s => s.trim() !== '')
      : undefined;

    columns.push({
      key,
      title: title && title.trim() !== '' ? title : key,
      type,
      ...(searchable ? { searchable: true } : {}),
      ...(options && options.length > 0 ? { options } : {}),
    });
  }

  return columns;
}

/**
 * Extracts searchable fields (`"x-search"`) from `definition.dataSchema`, expanding numeric properties into `_min` (`≥`)
 * and `_max` (`≤`) bounds and appending `createdFrom`/`createdTo` date range filters when searchable fields exist.
 */
export function workflowSearchColumns(definition: WorkflowDefinitionModel | null | undefined): WorkflowDataColumn[] {
  const schema = definition?.dataSchema ?? definition?.startInputSchema;
  const properties = schema?.['properties'];
  if (!isObject(properties)) {
    return [];
  }

  const result: WorkflowDataColumn[] = [];
  for (const col of workflowDataColumns(definition, true)) {
    if (col.searchable !== true) {
      continue;
    }

    const prop = properties[col.key];
    const mode = isObject(prop) && typeof prop['x-search'] === 'string' ? prop['x-search'] : null;

    if (col.options && col.options.length > 0) {
      result.push(col);
    } else if (col.type === 'number' || col.type === 'integer' || mode === 'range' || mode === 'min' || mode === 'max' || mode === 'gte' || mode === 'lte') {
      if (mode !== 'max' && mode !== 'lte') {
        result.push({ key: `${col.key}_min`, title: `Min ${col.title} (≥)`, type: col.type, searchable: true });
      }
      if (mode !== 'min' && mode !== 'gte') {
        result.push({ key: `${col.key}_max`, title: `Max ${col.title} (≤)`, type: col.type, searchable: true });
      }
    } else {
      result.push(col);
    }
  }

  const dateSearchFlag = schema?.['x-date-search'];
  if (dateSearchFlag === true || (dateSearchFlag !== false && result.length > 0)) {
    result.push({ key: 'createdFrom', title: 'Başlangıç tarihi', type: 'date', searchable: true });
    result.push({ key: 'createdTo', title: 'Bitiş tarihi', type: 'date', searchable: true });
  }

  return result;
}

/** Formats a projected workflow data property value for display in the instance grid. */
export function formatWorkflowDataCell(data: WorkflowData | null | undefined, column: WorkflowDataColumn): string {
  const value = data?.[column.key];
  if (value === null || value === undefined || value === '') {
    return '-';
  }

  if (typeof value === 'boolean') {
    return value ? 'Evet' : 'Hayır';
  }

  if (typeof value === 'number' && Number.isFinite(value)) {
    if (column.type === 'number' && !Number.isInteger(value)) {
      return trNumberFormat2.format(value);
    }

    return trNumberFormat0.format(value);
  }

  return typeof value === 'string' ? value : JSON.stringify(value);
}

