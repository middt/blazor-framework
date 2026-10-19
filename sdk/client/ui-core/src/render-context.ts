// TypeScript port of Middt.Framework.Ui.Abstractions/Rendering/UiRenderContext.cs (UiExpressions, UiRenderContext,
// UiFormats, UiUrl).

import { ExpressionUser, PortableExpression, PortableExpressionError } from './expression.ts';
import type { ExpressionContext, ExpressionUserInfo, RuntimeValue } from './expression.ts';
import { DECIMAL_MAX, decimalSeparator, decimalString, formatDecimalGeneral, formatNumberInvariant, isNullOrWhiteSpace } from './text.ts';
import type { UiNode } from './types.ts';

/** Expression variables of layouts. */
export const UiExpressions = {
  /** Page/view data (e.g. the workflow instance data). */
  data: 'data',
  /** Query string values of the page. */
  query: 'query',
  /** Current item of a grid/list. */
  row: 'row',
  /** Record loaded by a `record` widget. */
  record: 'record',
  /** Workflow instance (`instance.state`, `instance.key`, ...). */
  instance: 'instance',
} as const;

/** All layout variables (`data`, `query`, `row`, `record`, `instance`). */
export const UI_VARIABLES: readonly string[] = Object.freeze(Object.values(UiExpressions));

/** Shared evaluator of layouts (parsed expressions are cached). */
export const uiEvaluator: PortableExpression = new PortableExpression(UI_VARIABLES);

/** Value formats every renderer supports: N0–N6 (grouped), F0–F6 (fixed) and the date formats d, D, g, t. */
export const UiFormats = {
  dates: Object.freeze(['d', 'D', 'g', 't']) as readonly string[],
  isValid(format: string | null | undefined): boolean {
    return typeof format === 'string' && (UiFormats.dates.includes(format) || /^[NF][0-6]$/.test(format));
  },
} as const;

// Control characters and whitespace anywhere (browsers strip tabs/new lines from URLs: "/\t/evil.example" → "//evil.example").
// A plain space inside the URL is allowed (browsers percent-encode it), but not at either end – same rule as UiUrl in .NET.
const UNSAFE_URL_CHARS = /[\u0000-\u001f\u007f-\u009f\u00a0\u1680\u2000-\u200b\u2028\u2029\u202f\u205f\u3000\ufeff]|^ | $/u;

function isLocalPath(url: string): boolean {
  return url[0] === '/' && (url.length === 1 || (url[1] !== '/' && url[1] !== '\\'));
}

/** Link safety: only local paths and http(s) URLs are rendered as links. */
export const UiUrl = {
  isSafe(url: string | null | undefined): boolean {
    if (typeof url !== 'string' || url.length === 0 || UNSAFE_URL_CHARS.test(url)) {
      return false;
    }

    if (isLocalPath(url)) {
      return true;
    }

    try {
      const parsed = new URL(url);
      return parsed.protocol === 'https:' || parsed.protocol === 'http:';
    } catch {
      return false;
    }
  },

  /** True for local paths only (used for navigation from grid actions). */
  isLocal(url: string | null | undefined): boolean {
    return typeof url === 'string' && url.length > 0 && !UNSAFE_URL_CHARS.test(url) && isLocalPath(url);
  },
} as const;

export interface UiRenderContextOptions {
  user: ExpressionUserInfo | ExpressionUser;
  variables?: Record<string, unknown>;
  /** `false` for static rendering: interactive widgets render a placeholder. */
  interactive?: boolean;
  /** Time of `now` (default: the current time). */
  now?: Date | string | number;
  /** Prefix of generated element ids (default `ui`). */
  idPrefix?: string;
  /** Locale of formatted values (default `tr-TR`). */
  locale?: string;
  /** Time zone dates with an offset are shown in (default: the browser's). */
  timeZone?: string;
  /** Evaluator (default: the shared {@link uiEvaluator}). */
  evaluator?: PortableExpression;
}

// Intl formatters are expensive to construct: one per locale/options/time zone, shared by all render contexts.
const MAX_FORMATTERS = 256;
const numberFormats = new Map<string, Intl.NumberFormat>();
const dateFormats = new Map<string, Intl.DateTimeFormat>();

function cached<T>(cache: Map<string, T>, key: string, create: () => T): T {
  let value = cache.get(key);
  if (value === undefined) {
    if (cache.size >= MAX_FORMATTERS) {
      cache.clear();
    }

    value = create();
    cache.set(key, value);
  }

  return value;
}

function numberFormat(locale: string, format: string): Intl.NumberFormat {
  return cached(numberFormats, `${locale}|${format}`, () => {
    const digits = Number(format[1]);
    return new Intl.NumberFormat(locale, {
      minimumFractionDigits: digits,
      maximumFractionDigits: digits,
      useGrouping: format[0] === 'N' ? 'always' : false,
      signDisplay: 'negative',
      roundingMode: 'halfExpand',
    });
  });
}

type DateStyle = 'shortDate' | 'shortTime' | 'full';

const DATE_OPTIONS: Record<DateStyle, Intl.DateTimeFormatOptions> = {
  shortDate: { year: 'numeric', month: '2-digit', day: '2-digit' },
  shortTime: { hour: 'numeric', minute: '2-digit' },
  full: { dateStyle: 'full' },
};

function dateFormat(locale: string, style: DateStyle, timeZone: string | undefined): Intl.DateTimeFormat {
  return cached(dateFormats, `${locale}|${style}|${timeZone ?? ''}`, () =>
    new Intl.DateTimeFormat(locale, { ...DATE_OPTIONS[style], timeZone }));
}

const ISO_DATE = /^(\d{4})-(\d{2})-(\d{2})(?:[T ](\d{2}):(\d{2})(?::(\d{2})(?:\.(\d{1,7}))?)?)?(Z|[+-]\d{2}:?\d{2})?$/i;

interface ParsedDate {
  date: Date;
  /** Time zone to format in: undefined = local (instants), `UTC` = wall clock of an unzoned value. */
  zoned: boolean;
}

/** ISO 8601 date/time text (the subset of DateTime.TryParse that layouts use); unzoned values keep their wall clock. */
function parseIsoDate(text: string): ParsedDate | null {
  const match = ISO_DATE.exec(text.trim());
  if (match === null) {
    return null;
  }

  const [, y, mo, d, h = '0', mi = '0', s = '0', fraction = '', zone] = match;
  const ms = Number(fraction.padEnd(3, '0').slice(0, 3));
  const wallClock = Date.UTC(Number(y), Number(mo) - 1, Number(d), Number(h), Number(mi), Number(s), ms);
  const check = new Date(wallClock);
  if (Number.isNaN(wallClock) || check.getUTCDate() !== Number(d) || check.getUTCMonth() !== Number(mo) - 1 || Number(h) > 23 || Number(mi) > 59 || Number(s) > 59) {
    return null;
  }

  if (zone === undefined) {
    return { date: check, zoned: false };
  }

  if (zone.toUpperCase() === 'Z') {
    return { date: check, zoned: true };
  }

  const sign = zone[0] === '-' ? -1 : 1;
  const digits = zone.slice(1).replace(':', '');
  const offset = sign * (Number(digits.slice(0, 2)) * 60 + Number(digits.slice(2)));
  return { date: new Date(wallClock - offset * 60_000), zoned: true };
}

/** What a widget renders with: the variables, the caller and whether the host is interactive. */
export class UiRenderContext {
  readonly user: ExpressionUser;
  readonly interactive: boolean;
  readonly now: Date;
  readonly idPrefix: string;
  readonly locale: string;
  readonly timeZone: string | undefined;
  readonly evaluator: PortableExpression;
  readonly #variables: Record<string, unknown>;
  #expressionContext: ExpressionContext | undefined;

  constructor(options: UiRenderContextOptions) {
    this.user = options.user instanceof ExpressionUser ? options.user : new ExpressionUser(options.user);
    this.interactive = options.interactive ?? false;
    this.now = options.now === undefined ? new Date() : options.now instanceof Date ? options.now : new Date(options.now);
    this.idPrefix = options.idPrefix ?? 'ui';
    this.locale = options.locale ?? 'tr-TR';
    this.timeZone = options.timeZone;
    this.evaluator = options.evaluator ?? uiEvaluator;
    this.#variables = { ...(options.variables ?? {}) };
  }

  get variables(): Readonly<Record<string, unknown>> {
    return this.#variables;
  }

  get(name: string): unknown {
    return Object.prototype.hasOwnProperty.call(this.#variables, name) ? this.#variables[name] : null;
  }

  /** Copy with one more (or a replaced) variable, e.g. `row` for list items. */
  with(name: string, value: unknown): UiRenderContext {
    return new UiRenderContext({
      user: this.user,
      variables: { ...this.#variables, [name]: value },
      interactive: this.interactive,
      now: this.now,
      idPrefix: this.idPrefix,
      locale: this.locale,
      timeZone: this.timeZone,
      evaluator: this.evaluator,
    });
  }

  toExpressionContext(): ExpressionContext {
    // The variables, user and time never change after construction: one context per render context.
    return (this.#expressionContext ??= { variables: this.#variables, user: this.user, now: this.now });
  }

  evaluate(expression: string): RuntimeValue {
    return this.evaluator.evaluate(expression, this.toExpressionContext());
  }

  /** Renders a template value (objects/arrays recurse). */
  render(template: unknown): unknown {
    return this.evaluator.render(template, this.toExpressionContext());
  }

  /** Renders a text template; expression errors are returned as text so layout authors can see them. */
  text(template: string | null | undefined, format?: string | null): string {
    if (template === null || template === undefined || template === '') {
      return '';
    }

    if (!PortableExpression.isTemplate(template)) {
      return template;
    }

    try {
      return this.format(this.evaluator.render(template, this.toExpressionContext()), format);
    } catch (error) {
      if (error instanceof PortableExpressionError) {
        return `⚠ ${error.message}`;
      }

      throw error;
    }
  }

  /**
   * Formats a value with a portable format: numbers N0–N6 / F0–F6, ISO dates d, D, g, t (in {@link locale});
   * booleans as Evet/Hayır; objects and arrays as JSON.
   */
  format(value: unknown, format?: string | null): string {
    const valid = UiFormats.isValid(format) ? format! : null;
    if (value === null || value === undefined) {
      return '';
    }

    switch (typeof value) {
      case 'number':
        return this.#formatNumber(value, valid);
      case 'string':
        return valid !== null && UiFormats.dates.includes(valid) ? this.#formatDate(value, valid) : value;
      case 'boolean':
        return value ? 'Evet' : 'Hayır';
      default:
        return JSON.stringify(value) ?? '';
    }
  }

  /** Evaluates `visibleWhen`; a failing expression hides the node. */
  isVisible(nodeOrCondition: UiNode | string | null | undefined): boolean {
    const condition = typeof nodeOrCondition === 'string' || nodeOrCondition === null || nodeOrCondition === undefined
      ? nodeOrCondition
      : nodeOrCondition.visibleWhen;
    if (typeof condition !== 'string' || isNullOrWhiteSpace(condition)) {
      return true;
    }

    try {
      return this.evaluator.evaluateCondition(condition, this.toExpressionContext());
    } catch (error) {
      if (error instanceof PortableExpressionError) {
        return false;
      }

      throw error;
    }
  }

  idFor(node: Pick<UiNode, 'id' | 'widget'>, suffix?: string | null): string {
    return `${this.idPrefix}-${node.id ?? node.widget}${suffix === null || suffix === undefined ? '' : '-' + suffix}`;
  }

  #formatNumber(value: number, format: string | null): string {
    if (!Number.isFinite(value) || Math.abs(value) > DECIMAL_MAX) {
      return formatNumberInvariant(value);
    }

    if (format === null || !/^[NF]/.test(format)) {
      // C# throws for a date format on a number; the plain text is shown instead.
      return formatDecimalGeneral(value, decimalSeparator(this.locale));
    }

    // String input: Intl rounds the shortest decimal text (like .NET decimal), not the binary value (1.005 → 1,01).
    return numberFormat(this.locale, format).format(decimalString(value) as unknown as number);
  }

  #formatDate(text: string, format: string): string {
    const parsed = parseIsoDate(text);
    if (parsed === null) {
      return text;
    }

    const zone = parsed.zoned ? this.timeZone : 'UTC';
    const date = (style: DateStyle): string => dateFormat(this.locale, style, zone).format(parsed.date);
    const shortDate = (): string => date('shortDate');
    const shortTime = (): string => date('shortTime');
    switch (format) {
      case 'd':
        return shortDate();
      case 'D':
        return date('full');
      case 'g':
        return `${shortDate()} ${shortTime()}`;
      default:
        return shortTime();
    }
  }
}
