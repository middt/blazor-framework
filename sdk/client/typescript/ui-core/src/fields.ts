// TypeScript port of Middt.Framework.Ui.Abstractions/Model/UiFieldModels.cs (grid columns, form/search fields and
// their validation; messages are Turkish like the Blazor renderer).

import { changeCaseInvariant, DECIMAL_MAX, decimalSeparator, formatDecimalGeneral, isNullOrWhiteSpace } from './text.ts';

type JsonObject = Record<string, unknown>;

const isObject = (value: unknown): value is JsonObject => typeof value === 'object' && value !== null && !Array.isArray(value);

/** Helpers to read layout JSON values. */
export const UiFieldJson = {
  getString(obj: JsonObject, name: string): string | null {
    const value = obj[name];
    return typeof value === 'string' ? value : null;
  },

  getBool(obj: JsonObject, name: string): boolean | null {
    const value = obj[name];
    return typeof value === 'boolean' ? value : null;
  },

  getDecimal(obj: JsonObject, name: string): number | null {
    return UiFieldJson.toDecimal(obj[name]);
  },

  /** Number of a JSON number or of an invariant number text (`NumberStyles.Number`: sign, thousands, decimal point). */
  toDecimal(value: unknown): number | null {
    if (typeof value === 'number') {
      return Number.isFinite(value) && Math.abs(value) <= DECIMAL_MAX ? value : null;
    }

    if (typeof value === 'string') {
      const match = /^[\t-\r ]*([+-])?((?:\d[\d,]*)?(?:\.\d*)?)([+-])?[\t-\r ]*$/.exec(value);
      if (match === null || (match[1] !== undefined && match[3] !== undefined) || !/\d/.test(match[2]!)) {
        return null;
      }

      const number = Number(match[2]!.replaceAll(',', ''));
      const signed = (match[1] ?? match[3]) === '-' ? -number : number;
      return Number.isFinite(signed) && Math.abs(signed) <= DECIMAL_MAX ? signed : null;
    }

    return null;
  },

  /** Stable string key of a value (dropdown value): text as is, other values as JSON. */
  key(value: unknown): string {
    if (value === null || value === undefined) {
      return '';
    }

    return typeof value === 'string' ? value : (JSON.stringify(value) ?? '');
  },
} as const;

const cloneJson = (value: unknown): unknown =>
  value === undefined ? null : typeof value === 'object' && value !== null ? JSON.parse(JSON.stringify(value)) : value;

/** Grid column parsed from a layout (`{ field, title, format, width, sortable, template }`). */
export class UiColumn {
  field = '';
  title = '';
  format: string | null = null;
  width: string | null = null;
  sortable = true;
  template: string | null = null;

  /** Property name sent to the API for sorting (PascalCase of {@link field}). */
  get sortProperty(): string {
    return this.field.length === 0 ? this.field : changeCaseInvariant(this.field[0]!, true) + this.field.slice(1);
  }

  static parse(c: JsonObject): UiColumn {
    const column = new UiColumn();
    column.field = UiFieldJson.getString(c, 'field') ?? '';
    column.title = UiFieldJson.getString(c, 'title') ?? UiFieldJson.getString(c, 'field') ?? '';
    column.format = UiFieldJson.getString(c, 'format');
    column.width = UiFieldJson.getString(c, 'width');
    column.sortable = UiFieldJson.getBool(c, 'sortable') ?? true;
    column.template = UiFieldJson.getString(c, 'template');
    return column;
  }

  static parseAll(columns: unknown): UiColumn[] {
    return Array.isArray(columns) ? columns.filter(isObject).map(c => UiColumn.parse(c)) : [];
  }
}

/** A select option. */
export class UiOption {
  readonly value: unknown;
  readonly text: string;

  constructor(value: unknown, text: string) {
    this.value = value;
    this.text = text;
  }

  /** Stable string key of {@link value} (dropdown value). */
  get key(): string {
    return UiFieldJson.key(this.value);
  }
}

/** Options loaded from an entity (select) or picked in a dialog (picker). */
export interface UiLookup {
  entity: string;
  valueField: string;
  textField: string;
  title: string | null;
  columns: UiColumn[];
  search: UiField[];
}

/** Form/search field parsed from a layout. */
export class UiField {
  field = '';
  label = '';
  /** text, textarea, number, integer, date, datetime, checkbox, select or picker. */
  type = 'text';
  required = false;
  min: number | null = null;
  max: number | null = null;
  maxLength: number | null = null;
  placeholder: string | null = null;
  options: UiOption[] = [];
  lookup: UiLookup | null = null;

  static parseAll(fields: unknown): UiField[] {
    return Array.isArray(fields) ? fields.filter(isObject).map(f => UiField.parse(f)) : [];
  }

  static parse(f: JsonObject): UiField {
    const field = new UiField();
    field.field = UiFieldJson.getString(f, 'field') ?? '';
    field.label = UiFieldJson.getString(f, 'label') ?? UiFieldJson.getString(f, 'field') ?? '';
    field.type = UiFieldJson.getString(f, 'type') ?? 'text';
    field.required = UiFieldJson.getBool(f, 'required') ?? false;
    field.min = UiFieldJson.getDecimal(f, 'min');
    field.max = UiFieldJson.getDecimal(f, 'max');
    const maxLength = UiFieldJson.getDecimal(f, 'maxLength');
    field.maxLength = maxLength === null ? null : Math.trunc(maxLength);
    field.placeholder = UiFieldJson.getString(f, 'placeholder');
    field.options = (Array.isArray(f['options']) ? f['options'] : []).map((o: unknown) =>
      isObject(o)
        ? new UiOption(cloneJson(o['value']), UiFieldJson.getString(o, 'text') ?? UiFieldJson.key(o['value']))
        : new UiOption(cloneJson(o), UiFieldJson.key(o)));
    const lookup = f['lookup'];
    field.lookup = isObject(lookup)
      ? {
          entity: UiFieldJson.getString(lookup, 'entity') ?? '',
          valueField: UiFieldJson.getString(lookup, 'valueField') ?? '',
          textField: UiFieldJson.getString(lookup, 'textField') ?? '',
          title: UiFieldJson.getString(lookup, 'title'),
          columns: UiColumn.parseAll(lookup['columns']),
          search: UiField.parseAll(lookup['search']),
        }
      : null;
    return field;
  }

  /**
   * Validates the value of this field in `target`; returns the error or null.
   * @param locale Culture of the numbers in the messages (default `tr-TR`).
   */
  validate(target: JsonObject, locale = 'tr-TR'): string | null {
    const value = Object.prototype.hasOwnProperty.call(target, this.field) ? target[this.field] : null;
    const empty = value === null || value === undefined || (typeof value === 'string' && isNullOrWhiteSpace(value));
    if (empty) {
      return this.required && this.type !== 'checkbox' ? 'Bu alan zorunludur.' : null;
    }

    const number = this.type === 'number' || this.type === 'integer' ? UiFieldJson.toDecimal(value) : null;
    if (number !== null) {
      if (this.type === 'integer' && Math.trunc(number) !== number) {
        return 'Tam sayı olmalıdır.';
      }

      if (this.min !== null && number < this.min) {
        return `En az ${formatDecimalGeneral(this.min, decimalSeparator(locale))} olmalıdır.`;
      }

      if (this.max !== null && number > this.max) {
        return `En fazla ${formatDecimalGeneral(this.max, decimalSeparator(locale))} olabilir.`;
      }
    }

    if ((this.type === 'text' || this.type === 'textarea') && this.maxLength !== null && typeof value === 'string' && value.length > this.maxLength) {
      return `En fazla ${this.maxLength} karakter olabilir.`;
    }

    return null;
  }
}

/** Validates every field; returns the errors by field name (empty when valid). */
export function validateFields(fields: readonly UiField[], target: JsonObject, locale = 'tr-TR'): Record<string, string> {
  const errors: Record<string, string> = {};
  for (const field of fields) {
    const error = field.validate(target, locale);
    if (error !== null) {
      errors[field.field] = error;
    }
  }

  return errors;
}
