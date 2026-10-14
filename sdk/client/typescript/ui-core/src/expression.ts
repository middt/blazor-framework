// The portable expression language – TypeScript port of Middt.Framework.Expressions.Portable
// (PortableSyntax.cs, PortableEvaluator.cs, PortableExpression.cs). Both implementations run the shared conformance
// suite spec/expressions.conformance.json; keep them in sync.

import { changeCaseInvariant, formatNumberInvariant, isNullOrWhiteSpace, isWhiteSpace, trimWhiteSpace } from './text.ts';

/** Syntax error of a portable expression (`position` is the 0-based character offset). */
export class PortableExpressionError extends Error {
  readonly position: number;

  constructor(message: string, position: number) {
    super(`${message} (at ${position})`);
    this.name = 'PortableExpressionError';
    this.position = position;
  }
}

/** Plain description of the caller (as delivered by an API or a BFF). */
export interface ExpressionUserInfo {
  id?: string | null;
  name?: string | null;
  roles?: readonly string[];
  /** System actors pass every role check. */
  isSystem?: boolean;
}

/** The caller as seen by expressions: `user.Name`, `user.Id`, `user.Roles`, `user.IsInRole("manager")`. */
export class ExpressionUser {
  readonly id: string | null;
  readonly name: string | null;
  readonly roles: readonly string[];
  readonly isSystem: boolean;

  constructor(info: ExpressionUserInfo = {}) {
    this.id = info.id ?? null;
    this.name = info.name ?? null;
    this.roles = [...(info.roles ?? [])];
    this.isSystem = info.isSystem ?? false;
  }

  isInRole(role: string): boolean {
    return this.isSystem || this.roles.includes(role);
  }

  toString(): string {
    return this.name ?? this.id ?? '';
  }
}

/** Evaluation context: JSON variables, the caller and the current time (`now`, ISO UTC text). */
export interface ExpressionContext {
  variables?: Record<string, unknown>;
  /** Defaults to an anonymous user without roles (C# defaults to a system user; a frontend never should). */
  user?: ExpressionUserInfo | ExpressionUser;
  /** Defaults to the current time. */
  now?: Date | string | number;
}

/** Marker value of the `Math` identifier. */
class MathScope {}

const MATH = Object.freeze(new MathScope());

type JsonObject = Record<string, unknown>;

/** Runtime value: null, boolean, number, string, JSON object, array, the user or `Math`. */
export type RuntimeValue = null | boolean | number | string | JsonObject | unknown[] | ExpressionUser | MathScope;

// ---------------------------------------------------------------------------------------------------------------------
// Lexer

type TokenKind = 'number' | 'string' | 'identifier' | 'operator' | 'end';

interface Token {
  kind: TokenKind;
  text: string;
  position: number;
  number: number;
}

const OPERATORS = ['??', '&&', '||', '==', '!=', '<=', '>=', '<', '>', '+', '-', '*', '/', '%', '!', '?', ':', '.', ',', '(', ')', '[', ']'];

const isDigit = (c: string | undefined): boolean => c !== undefined && c >= '0' && c <= '9';
const isLetter = (c: string | undefined): boolean => c !== undefined && ((c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z'));
const NUMBER_LITERAL = /^(?:\d+(?:\.\d*)?|\.\d+)$/;

function tokenize(text: string): Token[] {
  const tokens: Token[] = [];
  let i = 0;
  while (i < text.length) {
    const c = text[i]!;
    if (isWhiteSpace(c)) {
      i++;
      continue;
    }

    if (isDigit(c) || (c === '.' && isDigit(text[i + 1]))) {
      const start = i;
      while (i < text.length && (isDigit(text[i]) || text[i] === '.')) {
        i++;
      }

      const literal = text.slice(start, i);
      if (!NUMBER_LITERAL.test(literal)) {
        throw new PortableExpressionError(`Invalid number '${literal}'`, start);
      }

      tokens.push({ kind: 'number', text: literal, position: start, number: Number(literal) });
      continue;
    }

    if (c === '"' || c === "'") {
      const start = i++;
      let value = '';
      while (i < text.length && text[i] !== c) {
        if (text[i] === '\\' && i + 1 < text.length) {
          i++;
          const escaped = text[i]!;
          value += escaped === 'n' ? '\n' : escaped === 't' ? '\t' : escaped === 'r' ? '\r' : escaped; // \" \' \\
        } else {
          value += text[i];
        }

        i++;
      }

      if (i >= text.length) {
        throw new PortableExpressionError('Unterminated string', start);
      }

      i++;
      tokens.push({ kind: 'string', text: value, position: start, number: 0 });
      continue;
    }

    if (isLetter(c) || c === '_') {
      const start = i;
      while (i < text.length && (isLetter(text[i]) || isDigit(text[i]) || text[i] === '_')) {
        i++;
      }

      tokens.push({ kind: 'identifier', text: text.slice(start, i), position: start, number: 0 });
      continue;
    }

    const op = OPERATORS.find(o => text.startsWith(o, i));
    if (op === undefined) {
      throw new PortableExpressionError(`Unexpected character '${c}'`, i);
    }

    tokens.push({ kind: 'operator', text: op, position: i, number: 0 });
    i += op.length;
  }

  tokens.push({ kind: 'end', text: '', position: text.length, number: 0 });
  return tokens;
}

// ---------------------------------------------------------------------------------------------------------------------
// Syntax tree and parser

type Node =
  | { type: 'literal'; value: null | boolean | number | string; position: number }
  | { type: 'variable'; name: string; position: number }
  | { type: 'member'; target: Node; name: string; position: number }
  | { type: 'index'; target: Node; index: Node; position: number }
  | { type: 'call'; target: Node; method: string; args: Node[]; position: number }
  | { type: 'unary'; operator: string; operand: Node; position: number }
  | { type: 'binary'; operator: string; left: Node; right: Node; position: number }
  | { type: 'conditional'; condition: Node; whenTrue: Node; whenFalse: Node; position: number };

/** Methods expressions may call (anything else is a syntax error). */
export const PORTABLE_METHODS: ReadonlySet<string> = new Set([
  'IsInRole', 'Contains', 'StartsWith', 'EndsWith', 'ToUpper', 'ToLower', 'Trim',
  'Round', 'Abs', 'Floor', 'Ceiling', 'Min', 'Max',
]);

const MAX_DEPTH = 64;

// Binary operators by precedence (lowest first).
const LEVELS: readonly (readonly string[])[] = [
  ['??'],
  ['||'],
  ['&&'],
  ['==', '!='],
  ['<', '<=', '>', '>='],
  ['+', '-'],
  ['*', '/', '%'],
];

/** Recursive descent parser; rejects unknown variables and methods so typos fail at validation time. */
class Parser {
  readonly #tokens: Token[];
  readonly #variables: ReadonlySet<string>;
  #index = 0;
  #depth = 0;

  constructor(text: string, variables: ReadonlySet<string>) {
    this.#tokens = tokenize(text);
    this.#variables = variables;
  }

  static parse(text: string | null | undefined, variables: ReadonlySet<string>): Node {
    if (typeof text !== 'string' || isNullOrWhiteSpace(text)) {
      throw new PortableExpressionError('Expression is empty', 0);
    }

    const parser = new Parser(text, variables);
    const node = parser.#expression();
    if (parser.#current.kind !== 'end') {
      throw new PortableExpressionError(`Unexpected '${parser.#current.text}'`, parser.#current.position);
    }

    return node;
  }

  get #current(): Token {
    return this.#tokens[this.#index]!;
  }

  #isOperator(op: string): boolean {
    return this.#current.kind === 'operator' && this.#current.text === op;
  }

  #accept(op: string): boolean {
    if (!this.#isOperator(op)) {
      return false;
    }

    this.#index++;
    return true;
  }

  #expect(op: string): void {
    if (!this.#accept(op)) {
      throw new PortableExpressionError(`Expected '${op}'`, this.#current.position);
    }
  }

  #expression(): Node {
    if (++this.#depth > MAX_DEPTH) {
      throw new PortableExpressionError('Expression is nested too deeply', this.#current.position);
    }

    try {
      const condition = this.#binary(0);
      if (this.#isOperator('?')) {
        const position = this.#current.position;
        this.#index++;
        const whenTrue = this.#expression();
        this.#expect(':');
        const whenFalse = this.#expression();
        return { type: 'conditional', condition, whenTrue, whenFalse, position };
      }

      return condition;
    } finally {
      this.#depth--;
    }
  }

  #binary(level: number): Node {
    if (level === LEVELS.length) {
      return this.#unary();
    }

    let left = this.#binary(level + 1);
    while (this.#current.kind === 'operator' && LEVELS[level]!.includes(this.#current.text)) {
      const op = this.#current;
      this.#index++;
      left = { type: 'binary', operator: op.text, left, right: this.#binary(level + 1), position: op.position };
    }

    return left;
  }

  #unary(): Node {
    if (this.#isOperator('!') || this.#isOperator('-')) {
      const op = this.#current;
      this.#index++;
      if (++this.#depth > MAX_DEPTH) {
        throw new PortableExpressionError('Expression is nested too deeply', op.position);
      }

      try {
        return { type: 'unary', operator: op.text, operand: this.#unary(), position: op.position };
      } finally {
        this.#depth--;
      }
    }

    return this.#postfix(this.#primary());
  }

  #postfix(node: Node): Node {
    while (true) {
      if (this.#isOperator('.')) {
        this.#index++;
        if (this.#current.kind !== 'identifier') {
          throw new PortableExpressionError('Expected a member name', this.#current.position);
        }

        const name = this.#current;
        this.#index++;
        if (this.#isOperator('(')) {
          if (!PORTABLE_METHODS.has(name.text)) {
            throw new PortableExpressionError(`Unknown method '${name.text}'`, name.position);
          }

          node = { type: 'call', target: node, method: name.text, args: this.#arguments(), position: name.position };
        } else {
          node = { type: 'member', target: node, name: name.text, position: name.position };
        }
      } else if (this.#isOperator('[')) {
        const position = this.#current.position;
        this.#index++;
        const key = this.#expression();
        this.#expect(']');
        node = { type: 'index', target: node, index: key, position };
      } else {
        return node;
      }
    }
  }

  #arguments(): Node[] {
    this.#expect('(');
    const args: Node[] = [];
    if (!this.#accept(')')) {
      do {
        args.push(this.#expression());
      } while (this.#accept(','));
      this.#expect(')');
    }

    return args;
  }

  #primary(): Node {
    const token = this.#current;
    switch (token.kind) {
      case 'number':
        this.#index++;
        return { type: 'literal', value: token.number, position: token.position };
      case 'string':
        this.#index++;
        return { type: 'literal', value: token.text, position: token.position };
      case 'identifier':
        this.#index++;
        switch (token.text) {
          case 'true':
            return { type: 'literal', value: true, position: token.position };
          case 'false':
            return { type: 'literal', value: false, position: token.position };
          case 'null':
            return { type: 'literal', value: null, position: token.position };
          case 'Math':
            return { type: 'variable', name: 'Math', position: token.position };
        }

        if (!this.#variables.has(token.text)) {
          throw new PortableExpressionError(`Unknown variable '${token.text}'`, token.position);
        }

        return { type: 'variable', name: token.text, position: token.position };
      case 'operator':
        if (token.text === '(') {
          this.#index++;
          const inner = this.#expression();
          this.#expect(')');
          return inner;
        }

        throw new PortableExpressionError(`Unexpected '${token.text}'`, token.position);
      case 'end':
        throw new PortableExpressionError('Unexpected end of expression', token.position);
    }
  }
}

// ---------------------------------------------------------------------------------------------------------------------
// Evaluator (never throws: anything undefined yields null)

const hasOwn = (obj: object, key: string): boolean => Object.prototype.hasOwnProperty.call(obj, key);

function isJsonObject(value: unknown): value is JsonObject {
  return typeof value === 'object' && value !== null && !Array.isArray(value) && !(value instanceof ExpressionUser) && !(value instanceof MathScope);
}

/** Runtime value of a JSON value (undefined → null, Date → ISO text, unsupported values → null). */
function fromJson(value: unknown): RuntimeValue {
  switch (typeof value) {
    case 'string':
    case 'boolean':
    case 'number':
      return value;
    case 'bigint':
      return Number(value);
    case 'object':
      if (value === null) {
        return null;
      }

      if (value instanceof Date) {
        return Number.isNaN(value.getTime()) ? null : value.toISOString();
      }

      return value as JsonObject | unknown[];
    default:
      return null;
  }
}

/** Deep copy of JSON data (JSON semantics: undefined members are dropped, Dates become ISO text). */
function cloneJson(value: unknown): unknown {
  if (value === undefined) {
    return null;
  }

  return typeof value === 'object' && value !== null ? JSON.parse(JSON.stringify(value)) : value;
}

/** JSON value of a runtime value (integers normalized, the user as `{ id, name, roles }`, `Math` → null). */
function toJson(value: RuntimeValue): unknown {
  if (value === null || typeof value === 'boolean' || typeof value === 'string') {
    return value;
  }

  if (typeof value === 'number') {
    return Number.isFinite(value) ? (value === 0 ? 0 : value) : null;
  }

  if (value instanceof ExpressionUser) {
    return { id: value.id, name: value.name, roles: [...value.roles] };
  }

  if (value instanceof MathScope) {
    return null;
  }

  return cloneJson(value);
}

/** null, false, 0, NaN and "" are false; everything else (including objects and arrays) is true. */
function isTruthy(value: RuntimeValue): boolean {
  if (value === null) {
    return false;
  }

  switch (typeof value) {
    case 'boolean':
      return value;
    case 'number':
      return value !== 0 && !Number.isNaN(value);
    case 'string':
      return value.length > 0;
    default:
      return true;
  }
}

function toText(value: unknown): string {
  if (value === null || value === undefined) {
    return '';
  }

  switch (typeof value) {
    case 'string':
      return value;
    case 'boolean':
      return value ? 'true' : 'false';
    case 'number':
      return formatNumberInvariant(value);
    case 'bigint':
      return formatNumberInvariant(Number(value));
    case 'object':
      if (value instanceof ExpressionUser) {
        return value.name ?? value.id ?? '';
      }

      if (value instanceof MathScope) {
        return ''; // C# prints the .NET type name here
      }

      if (value instanceof Date) {
        return Number.isNaN(value.getTime()) ? '' : value.toISOString();
      }

      return JSON.stringify(value) ?? '';
    default:
      return '';
  }
}

/** Strict equality without coercion: `"5" == 5` is false; objects and arrays are equal only to themselves. */
function areEqual(left: RuntimeValue, right: RuntimeValue): boolean {
  if (left === null || right === null) {
    return left === right;
  }

  // numbers (NaN != NaN, 0 == -0), strings (ordinal), booleans; otherwise reference equality
  return left === right;
}

function compare(op: string, left: RuntimeValue, right: RuntimeValue): boolean {
  let order: number;
  if (typeof left === 'number' && typeof right === 'number') {
    if (Number.isNaN(left) || Number.isNaN(right)) {
      return false;
    }

    order = left < right ? -1 : left > right ? 1 : 0;
  } else if (typeof left === 'string' && typeof right === 'string') {
    order = left < right ? -1 : left > right ? 1 : 0; // UTF-16 code unit order = string.CompareOrdinal
  } else {
    return false;
  }

  switch (op) {
    case '<':
      return order < 0;
    case '<=':
      return order <= 0;
    case '>':
      return order > 0;
    default:
      return order >= 0;
  }
}

function arithmetic(left: RuntimeValue, right: RuntimeValue, op: (a: number, b: number) => number): RuntimeValue {
  if (typeof left !== 'number' || typeof right !== 'number') {
    return null;
  }

  const result = op(left, right);
  return Number.isFinite(result) ? result : null;
}

/** `Math.Round(value, digits, MidpointRounding.AwayFromZero)` of .NET (same floating point steps). */
function roundAwayFromZero(value: number, digits: number): number {
  if (Math.abs(value) >= 1e16) {
    return value;
  }

  const power10 = 10 ** digits;
  let scaled = value * power10;
  scaled = Math.trunc(scaled + (scaled < 0 || Object.is(scaled, -0) ? -0.49999999999999994 : 0.49999999999999994));
  return scaled / power10;
}

function mathCall(method: string, args: RuntimeValue[]): RuntimeValue {
  const [a, b] = args;
  const one = args.length === 1 && typeof a === 'number';
  const two = args.length === 2 && typeof a === 'number' && typeof b === 'number';
  switch (method) {
    case 'Round':
      if (one) {
        return roundAwayFromZero(a, 0);
      }

      return two && b >= 0 && b <= 15 && Math.floor(b) === b ? roundAwayFromZero(a, b) : null;
    case 'Abs':
      return one ? Math.abs(a) : null;
    case 'Floor':
      return one ? Math.floor(a) : null;
    case 'Ceiling':
      return one ? Math.ceil(a) : null;
    case 'Min':
      return two ? Math.min(a, b) : null;
    case 'Max':
      return two ? Math.max(a, b) : null;
    default:
      return null;
  }
}

function call(target: RuntimeValue, method: string, args: RuntimeValue[]): RuntimeValue {
  if (target instanceof ExpressionUser) {
    return method === 'IsInRole' && args.length === 1 && typeof args[0] === 'string' ? target.isInRole(args[0]) : null;
  }

  if (typeof target === 'string') {
    const [x] = args;
    if (args.length === 1 && typeof x === 'string') {
      switch (method) {
        case 'Contains':
          return target.includes(x);
        case 'StartsWith':
          return target.startsWith(x);
        case 'EndsWith':
          return target.endsWith(x);
      }
    } else if (args.length === 0) {
      switch (method) {
        case 'ToUpper':
          return changeCaseInvariant(target, true);
        case 'ToLower':
          return changeCaseInvariant(target, false);
        case 'Trim':
          return trimWhiteSpace(target);
      }
    }

    return null;
  }

  if (Array.isArray(target)) {
    return method === 'Contains' && args.length === 1 ? target.some(item => areEqual(fromJson(item), args[0]!)) : null;
  }

  return target instanceof MathScope ? mathCall(method, args) : null;
}

function member(target: RuntimeValue, name: string): RuntimeValue {
  if (Array.isArray(target)) {
    return name === 'Length' || name === 'Count' ? target.length : null;
  }

  if (typeof target === 'string') {
    return name === 'Length' ? target.length : null;
  }

  if (target instanceof ExpressionUser) {
    switch (name) {
      case 'Name':
        return target.name;
      case 'Id':
        return target.id;
      case 'Roles':
        return [...target.roles];
      default:
        return null;
    }
  }

  return isJsonObject(target) && hasOwn(target, name) ? fromJson(target[name]) : null;
}

function index(target: RuntimeValue, key: RuntimeValue): RuntimeValue {
  if (isJsonObject(target) && typeof key === 'string') {
    return hasOwn(target, key) ? fromJson(target[key]) : null;
  }

  if (typeof key === 'number' && Math.floor(key) === key && key >= 0) {
    if (Array.isArray(target) && key < target.length) {
      return fromJson(target[key]);
    }

    if (typeof target === 'string' && key < target.length) {
      return target[key]!;
    }
  }

  return null;
}

interface Scope {
  variables: Record<string, unknown>;
  user: ExpressionUser;
  now: string;
}

function evaluateNode(node: Node, scope: Scope): RuntimeValue {
  switch (node.type) {
    case 'literal':
      return node.value;
    case 'variable':
      if (node.name === 'Math') {
        return MATH;
      }

      if (!hasOwn(scope.variables, node.name)) {
        return node.name === 'user' ? scope.user : node.name === 'now' ? scope.now : null;
      }

      return fromJson(scope.variables[node.name]);
    case 'member':
      return member(evaluateNode(node.target, scope), node.name);
    case 'index':
      return index(evaluateNode(node.target, scope), evaluateNode(node.index, scope));
    case 'call':
      return call(evaluateNode(node.target, scope), node.method, node.args.map(a => evaluateNode(a, scope)));
    case 'unary': {
      const value = evaluateNode(node.operand, scope);
      if (node.operator === '!') {
        return !isTruthy(value);
      }

      return typeof value === 'number' ? -value : null;
    }
    case 'conditional':
      return isTruthy(evaluateNode(node.condition, scope)) ? evaluateNode(node.whenTrue, scope) : evaluateNode(node.whenFalse, scope);
    case 'binary':
      return binary(node.operator, node.left, node.right, scope);
  }
}

function binary(op: string, leftNode: Node, rightNode: Node, scope: Scope): RuntimeValue {
  // short-circuit operators
  switch (op) {
    case '&&':
      return isTruthy(evaluateNode(leftNode, scope)) && isTruthy(evaluateNode(rightNode, scope));
    case '||':
      return isTruthy(evaluateNode(leftNode, scope)) || isTruthy(evaluateNode(rightNode, scope));
    case '??':
      return evaluateNode(leftNode, scope) ?? evaluateNode(rightNode, scope);
  }

  const left = evaluateNode(leftNode, scope);
  const right = evaluateNode(rightNode, scope);
  switch (op) {
    case '==':
      return areEqual(left, right);
    case '!=':
      return !areEqual(left, right);
    case '<':
    case '<=':
    case '>':
    case '>=':
      return compare(op, left, right);
    case '+':
      return typeof left === 'string' || typeof right === 'string' ? toText(left) + toText(right) : arithmetic(left, right, (a, b) => a + b);
    case '-':
      return arithmetic(left, right, (a, b) => a - b);
    case '*':
      return arithmetic(left, right, (a, b) => a * b);
    case '/':
      return right === 0 ? null : arithmetic(left, right, (a, b) => a / b);
    case '%':
      return right === 0 ? null : arithmetic(left, right, (a, b) => a % b);
    default:
      return null;
  }
}

function toIsoNow(now: Date | string | number | undefined): string {
  const date = now === undefined ? new Date() : now instanceof Date ? now : new Date(now);
  return Number.isNaN(date.getTime()) ? '' : date.toISOString(); // yyyy-MM-ddTHH:mm:ss.fffZ
}

interface CachedScope {
  variables: ExpressionContext['variables'];
  user: ExpressionContext['user'];
  now: string | number;
  scope: Scope;
}

// Contexts that are reused (e.g. UiRenderContext) get their scope built once; the entry is checked against the context's
// current members, so a context changed in place is never evaluated with a stale scope. Contexts without `now` are
// not cached (their `now` is the current time).
const scopes = new WeakMap<ExpressionContext, CachedScope>();

function toScope(context: ExpressionContext): Scope {
  if (context.now === undefined) {
    return createScope(context);
  }

  const now = context.now instanceof Date ? context.now.getTime() : context.now;
  const cached = scopes.get(context);
  if (cached !== undefined && cached.variables === context.variables && cached.user === context.user && cached.now === now) {
    return cached.scope;
  }

  const scope = createScope(context);
  scopes.set(context, { variables: context.variables, user: context.user, now, scope });
  return scope;
}

function createScope(context: ExpressionContext): Scope {
  return {
    variables: context.variables ?? {},
    user: context.user instanceof ExpressionUser ? context.user : new ExpressionUser(context.user ?? { name: 'anonymous' }),
    now: toIsoNow(context.now),
  };
}

// ---------------------------------------------------------------------------------------------------------------------
// Public surface (same as the C# PortableExpression)

// {{ expression }}; whitespace is the .NET regex \s ([\f\n\r\t\v\x85\p{Z}]), '.' matches new lines (Singleline).
const PLACEHOLDER_SOURCE = '\\{\\{[\\f\\n\\r\\t\\v\\x85\\p{Z}]*([^]+?)[\\f\\n\\r\\t\\v\\x85\\p{Z}]*\\}\\}';
const PLACEHOLDER_TEST = new RegExp(PLACEHOLDER_SOURCE, 'u');
const PLACEHOLDER_ALL = new RegExp(PLACEHOLDER_SOURCE, 'gu');

/** Small bounded map that evicts the least recently used entry (keeps caches of user supplied text bounded). */
export class LruCache<K, V> {
  readonly #map = new Map<K, V>();
  readonly capacity: number;

  constructor(capacity: number) {
    this.capacity = Math.max(1, Math.trunc(capacity));
  }

  get size(): number {
    return this.#map.size;
  }

  get(key: K): V | undefined {
    const value = this.#map.get(key);
    if (value !== undefined) {
      // Re-insert: Map iteration order is insertion order, so the first key is the least recently used one.
      this.#map.delete(key);
      this.#map.set(key, value);
    }

    return value;
  }

  set(key: K, value: V): void {
    this.#map.delete(key);
    this.#map.set(key, value);
    if (this.#map.size > this.capacity) {
      this.#map.delete(this.#map.keys().next().value as K);
    }
  }
}

/** Parsed template: literal text around placeholders (`literals.length === expressions.length + 1`). */
interface TemplateSegments {
  literals: readonly string[];
  expressions: readonly string[];
  /** The template is exactly one placeholder (the value keeps its type). */
  whole: boolean;
}

/** Bound of the parsed template and parsed expression caches. */
export const EXPRESSION_CACHE_SIZE = 1000;

const templateCache = new LruCache<string, TemplateSegments>(EXPRESSION_CACHE_SIZE);

function segments(text: string): TemplateSegments {
  let parsed = templateCache.get(text);
  if (parsed === undefined) {
    const literals: string[] = [];
    const expressions: string[] = [];
    let last = 0;
    PLACEHOLDER_ALL.lastIndex = 0;
    for (const match of text.matchAll(PLACEHOLDER_ALL)) {
      literals.push(text.slice(last, match.index));
      expressions.push(match[1]!);
      last = match.index! + match[0].length;
    }

    literals.push(text.slice(last));
    parsed = { literals, expressions, whole: expressions.length === 1 && literals[0] === '' && literals[1] === '' };
    templateCache.set(text, parsed);
  }

  return parsed;
}

/**
 * The **portable expression language**: a small, sandboxed language with identical semantics in .NET
 * (`Middt.Framework.Expressions.Portable.PortableExpression`) and here, so every frontend evaluates layouts identically.
 *
 * - Literals: numbers, `"text"` / `'text'`, `true`, `false`, `null`
 * - Variables (declared per engine) plus `user` (`Name`, `Id`, `Roles`, `IsInRole(r)`) and `now` (ISO UTC text)
 * - Member access `a.b`, indexers `a["b"]`, `a[0]` (missing members and null targets yield null); `.Length` / `.Count`
 * - Operators `! -`, `* / %`, `+ -` (`+` concatenates when one side is text), `< <= > >=`, `== !=` (strict),
 *   `&& ||` (truthiness, boolean result), `??`, `?:`
 * - Methods: text `Contains StartsWith EndsWith ToUpper ToLower Trim`, array `Contains`,
 *   `Math.Round(x[, digits]) Abs Floor Ceiling Min Max` (rounding is half away from zero)
 * - Division by zero and invalid operands yield null – evaluation never throws (only syntax errors do).
 *
 * Templates embed expressions in text: `"Order #{{ data.orderId }}"`. A template that is exactly one placeholder keeps
 * the value's type.
 */
export class PortableExpression {
  readonly #variables: Set<string>;
  readonly #cache = new LruCache<string, Node>(EXPRESSION_CACHE_SIZE);

  /** @param variables Variables expressions may use (in addition to `user`, `now` and `Math`). */
  constructor(variables: Iterable<string>) {
    this.#variables = new Set(variables);
    this.#variables.add('user');
    this.#variables.add('now');
  }

  get variables(): ReadonlySet<string> {
    return this.#variables;
  }

  /** Parses `expression`; returns the error message or null when it is valid. */
  validate(expression: string): string | null {
    try {
      this.#parse(expression);
      return null;
    } catch (error) {
      if (error instanceof PortableExpressionError) {
        return error.message;
      }

      throw error;
    }
  }

  /** Validates every `{{ expression }}` of a template value (objects and arrays recurse). */
  validateTemplate(template: unknown): string[] {
    const errors: string[] = [];
    for (const expression of collectPlaceholders(template)) {
      const error = this.validate(expression);
      if (error !== null) {
        errors.push(error);
      }
    }

    return errors;
  }

  /** Evaluates to a runtime value (null, boolean, number, string, object, array, the user). Throws only on syntax errors. */
  evaluate(expression: string, context: ExpressionContext): RuntimeValue {
    return evaluateNode(this.#parse(expression), toScope(context));
  }

  #evaluateIn(expression: string, scope: Scope): RuntimeValue {
    return evaluateNode(this.#parse(expression), scope);
  }

  /** Evaluates to a JSON value (a copy: changing it does not change the variables). */
  evaluateToValue(expression: string, context: ExpressionContext): unknown {
    return toJson(this.evaluate(expression, context));
  }

  /** Truthiness of the result (null, false, 0 and "" are false). */
  evaluateCondition(expression: string, context: ExpressionContext): boolean {
    return isTruthy(this.evaluate(expression, context));
  }

  /** Renders a template value: strings with placeholders are evaluated, objects and arrays recurse, other values are copied. */
  render(template: unknown, context: ExpressionContext): unknown {
    if (typeof template === 'string' && !PLACEHOLDER_TEST.test(template)) {
      return template;
    }

    return this.#render(template, toScope(context));
  }

  #render(template: unknown, scope: Scope): unknown {
    if (Array.isArray(template)) {
      return template.map(item => this.#render(item, scope));
    }

    if (typeof template === 'string') {
      return this.#renderString(template, scope);
    }

    if (isJsonObject(template) && !(template instanceof Date)) {
      const result: JsonObject = {};
      for (const [key, value] of Object.entries(template)) {
        if (value !== undefined) {
          result[key] = this.#render(value, scope);
        }
      }

      return result;
    }

    return cloneJson(template);
  }

  /** Renders a text template to text. */
  renderText(text: string, context: ExpressionContext): string {
    return toText(fromJson(this.#renderString(text, toScope(context))));
  }

  /** Text of a runtime value as used in templates (integers without decimals, objects and arrays as JSON). */
  static toText(value: unknown): string {
    return toText(value);
  }

  /** null, false, 0, NaN and "" are false; everything else (including objects and arrays) is true. */
  static isTruthy(value: unknown): boolean {
    return isTruthy(fromJson(value));
  }

  /** True when `text` contains `{{ }}` placeholders. */
  static isTemplate(text: string | null | undefined): boolean {
    return typeof text === 'string' && PLACEHOLDER_TEST.test(text);
  }

  #renderString(text: string, scope: Scope): unknown {
    const { literals, expressions, whole } = segments(text);
    if (expressions.length === 0) {
      return text;
    }

    if (whole) {
      return toJson(this.#evaluateIn(expressions[0]!, scope));
    }

    let result = literals[0]!;
    for (let i = 0; i < expressions.length; i++) {
      result += toText(this.#evaluateIn(expressions[i]!, scope)) + literals[i + 1]!;
    }

    return result;
  }

  #parse(expression: string): Node {
    let node = this.#cache.get(expression);
    if (node === undefined) {
      node = Parser.parse(expression, this.#variables);
      this.#cache.set(expression, node);
    }

    return node;
  }
}

function* collectPlaceholders(template: unknown): Generator<string> {
  if (typeof template === 'string') {
    yield* segments(template).expressions;
  } else if (Array.isArray(template)) {
    for (const item of template) {
      yield* collectPlaceholders(item);
    }
  } else if (isJsonObject(template)) {
    for (const value of Object.values(template)) {
      yield* collectPlaceholders(value);
    }
  }
}
