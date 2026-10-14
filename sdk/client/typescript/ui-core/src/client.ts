// Minimal fetch based clients of the layout API (Middt.Framework.Ui.Server/Endpoints/UiEndpoints.cs) and of the CRUD
// controllers (Middt.Framework.Client/Http/UiClients.cs). Like the .NET ApiClientBase, failures never throw: they are
// returned as an envelope with `result: -1` and the messages (ProblemDetails / validation errors / network failure).

import { ResultEnum } from './types.ts';
import type {
  BaseResponseDataModel,
  BaseResponseModel,
  SearchRequest,
  UiLayoutDocument,
  UiLayoutRevision,
  UiLayoutSummary,
  UiValidationResult,
  UiWidgetSpecModel,
} from './types.ts';

export type FetchLike = (input: string, init?: RequestInit) => Promise<Response>;

export interface ApiClientOptions {
  /** Base URL of the API (`''` = same origin, e.g. behind a BFF). */
  baseUrl?: string;
  /** fetch implementation (default: the global fetch). */
  fetch?: FetchLike;
  /** Extra headers per request, e.g. `{ Authorization: 'Bearer …' }` (may be async). */
  headers?: () => Record<string, string> | Promise<Record<string, string>>;
  /** `include` for cookie authentication through a BFF (default `same-origin`). */
  credentials?: RequestCredentials;
  /** Called once on 401; return true to retry the request (e.g. after refreshing a token). */
  onUnauthorized?: () => boolean | Promise<boolean>;
}

export const UNAVAILABLE_MESSAGE = 'Servise erişilemedi. Lütfen daha sonra tekrar deneyin.';
export const EMPTY_RESPONSE_MESSAGE = 'Servis boş yanıt döndü.';

function failure<T extends BaseResponseModel>(...messages: string[]): T {
  return { result: ResultEnum.error, messageList: messages, count: 0, data: null } as unknown as T;
}

function joinUrl(baseUrl: string, path: string): string {
  if (baseUrl === '') {
    return path.startsWith('/') ? path : `/${path}`;
  }

  return `${baseUrl.replace(/\/+$/, '')}/${path.replace(/^\/+/, '')}`;
}

/**
 * Encodes one URL path segment. Empty, `.` and `..` are rejected: they pass encodeURIComponent unchanged and would
 * collapse a segment of the request path (e.g. `layouts/..` → the parent route).
 */
export function pathSegment(value: string, name = 'Path segment'): string {
  if (typeof value !== 'string' || value === '' || value === '.' || value === '..') {
    throw new Error(`${name} '${String(value)}' is not a valid URL path segment.`);
  }

  return encodeURIComponent(value);
}

const isAbort = (error: unknown): boolean => error instanceof Error && error.name === 'AbortError';

/** Shared request/response handling (JSON bodies, header hook, 401 retry, ProblemDetails → messages). */
export class ApiClientBase {
  readonly baseUrl: string;
  readonly #fetch: FetchLike;
  readonly #options: ApiClientOptions;

  constructor(options: ApiClientOptions = {}) {
    this.baseUrl = options.baseUrl ?? '';
    this.#fetch = options.fetch ?? ((input, init) => globalThis.fetch(input, init));
    this.#options = options;
  }

  /** Sends a request and reads the response envelope; never throws except when `signal` aborts. */
  protected async send<T extends BaseResponseModel>(method: string, path: string, body?: unknown, signal?: AbortSignal): Promise<T> {
    try {
      let response = await this.#request(method, path, body, signal);
      if (response.status === 401 && this.#options.onUnauthorized !== undefined && (await this.#options.onUnauthorized())) {
        response = await this.#request(method, path, body, signal);
      }

      return await ApiClientBase.readResponse<T>(response);
    } catch (error) {
      if (isAbort(error) && signal?.aborted) {
        throw error;
      }

      return failure<T>(UNAVAILABLE_MESSAGE);
    }
  }

  /** Sends a request and returns the raw response (for non-envelope endpoints). */
  protected async sendRaw(method: string, path: string, signal?: AbortSignal): Promise<Response> {
    let response = await this.#request(method, path, undefined, signal);
    if (response.status === 401 && this.#options.onUnauthorized !== undefined && (await this.#options.onUnauthorized())) {
      response = await this.#request(method, path, undefined, signal);
    }

    return response;
  }

  async #request(method: string, path: string, body: unknown, signal: AbortSignal | undefined): Promise<Response> {
    const headers: Record<string, string> = { Accept: 'application/json', ...(await this.#options.headers?.()) };
    const init: RequestInit = { method, headers, credentials: this.#options.credentials ?? 'same-origin' };
    if (body !== undefined) {
      headers['Content-Type'] = 'application/json';
      init.body = JSON.stringify(body);
    }

    if (signal !== undefined) {
      init.signal = signal;
    }

    return this.#fetch(joinUrl(this.baseUrl, path), init);
  }

  /** Maps a response to an envelope (non-success statuses become failures with the ProblemDetails messages). */
  static async readResponse<T extends BaseResponseModel>(response: Response): Promise<T> {
    if (response.ok) {
      const text = await response.text();
      if (text.trim() === '') {
        return failure<T>(EMPTY_RESPONSE_MESSAGE);
      }

      const parsed: unknown = JSON.parse(text);
      if (typeof parsed !== 'object' || parsed === null) {
        return failure<T>(EMPTY_RESPONSE_MESSAGE);
      }

      const envelope = parsed as T;
      envelope.messageList ??= [];
      return envelope;
    }

    const messages: string[] = [];
    let problem: { title?: unknown; detail?: unknown; errors?: unknown } | null = null;
    const mediaType = (response.headers.get('content-type') ?? '').split(';')[0]!.trim().toLowerCase();
    if (mediaType === 'application/problem+json' || mediaType === 'application/json') {
      try {
        const parsed: unknown = JSON.parse(await response.text());
        problem = typeof parsed === 'object' && parsed !== null ? parsed : null;
      } catch {
        problem = null;
      }
    }

    if (problem !== null && typeof problem.errors === 'object' && problem.errors !== null) {
      for (const value of Object.values(problem.errors)) {
        if (Array.isArray(value)) {
          messages.push(...value.filter((m): m is string => typeof m === 'string'));
        }
      }
    }

    if (messages.length === 0) {
      const text = [problem?.detail, problem?.title, response.statusText].find(m => typeof m === 'string' && m !== '');
      messages.push(typeof text === 'string' ? text : String(response.status));
    }

    return failure<T>(...messages);
  }
}

export interface UiApiClientOptions extends ApiClientOptions {
  /** Route prefix of `MapMiddtUi` (default `api/ui`). */
  routePrefix?: string;
}

/** Client of the layout API mapped by `MapMiddtUi` (default prefix `api/ui`). */
export class UiApiClient extends ApiClientBase {
  readonly routePrefix: string;

  constructor(options: UiApiClientOptions = {}) {
    super(options);
    this.routePrefix = (options.routePrefix ?? 'api/ui').replace(/^\/+|\/+$/g, '');
  }

  /** Layouts the user may open (`kind`: `page` or `view`). */
  list(kind?: string | null, signal?: AbortSignal): Promise<BaseResponseDataModel<UiLayoutSummary[]>> {
    const query = kind === undefined || kind === null ? '' : `?kind=${encodeURIComponent(kind)}`;
    return this.send('GET', `${this.routePrefix}/layouts${query}`, undefined, signal);
  }

  get(key: string, signal?: AbortSignal): Promise<BaseResponseDataModel<UiLayoutDocument>> {
    return this.send('GET', `${this.routePrefix}/layouts/${pathSegment(key, 'Layout key')}`, undefined, signal);
  }

  /** Validates a layout (admin). */
  validate(layout: UiLayoutDocument, signal?: AbortSignal): Promise<BaseResponseDataModel<UiValidationResult>> {
    return this.send('POST', `${this.routePrefix}/layouts/validate`, layout, signal);
  }

  /** Creates or updates a layout (admin); `layout.version` must be the loaded version (0 for a new layout). */
  save(layout: UiLayoutDocument, signal?: AbortSignal): Promise<BaseResponseDataModel<UiLayoutDocument>> {
    return this.send('PUT', `${this.routePrefix}/layouts/${pathSegment(layout.key, 'Layout key')}`, layout, signal);
  }

  /** Deletes a layout (admin); `version` enables the optimistic concurrency check. */
  delete(key: string, version?: number | null, signal?: AbortSignal): Promise<BaseResponseModel> {
    const query = version === undefined || version === null ? '' : `?version=${Math.trunc(version)}`;
    return this.send('DELETE', `${this.routePrefix}/layouts/${pathSegment(key, 'Layout key')}${query}`, undefined, signal);
  }

  /** Previous versions of a layout (admin). */
  revisions(key: string, signal?: AbortSignal): Promise<BaseResponseDataModel<UiLayoutRevision[]>> {
    return this.send('GET', `${this.routePrefix}/layouts/${pathSegment(key, 'Layout key')}/revisions`, undefined, signal);
  }

  /** Widget specs of the server catalog. */
  widgets(signal?: AbortSignal): Promise<BaseResponseDataModel<UiWidgetSpecModel[]>> {
    return this.send('GET', `${this.routePrefix}/widgets`, undefined, signal);
  }

  /** JSON Schema of layouts (for editors); null when it cannot be loaded. */
  async schema(signal?: AbortSignal): Promise<Record<string, unknown> | null> {
    try {
      const response = await this.sendRaw('GET', `${this.routePrefix}/schema`, signal);
      if (!response.ok) {
        return null;
      }

      const parsed: unknown = JSON.parse(await response.text());
      return typeof parsed === 'object' && parsed !== null && !Array.isArray(parsed) ? (parsed as Record<string, unknown>) : null;
    } catch (error) {
      if (isAbort(error) && signal?.aborted) {
        throw error;
      }

      return null;
    }
  }
}

export interface UiDataClientOptions extends ApiClientOptions {
  /** API version segment (`api/v{version}/…`, default `1.0` like `ApiSettings.Version`). */
  apiVersion?: string;
}

/** Entity (controller) names: letters and digits, starting with a letter. */
export const ENTITY_NAME = /^[A-Za-z][A-Za-z0-9]{0,63}$/;

type Row = Record<string, unknown>;

/**
 * Untyped client of the CRUD controllers (`api/v{version}/{entity}/getitems|getall|getbyid|insert|update|delete`)
 * used by layout widgets. Requests run as the signed-in user, so the API's authorization applies unchanged.
 */
export class UiDataClient extends ApiClientBase {
  readonly apiVersion: string;

  constructor(options: UiDataClientOptions = {}) {
    super(options);
    this.apiVersion = options.apiVersion ?? '1.0';
  }

  getItems<T = Row>(entity: string, searchRequest: SearchRequest, signal?: AbortSignal): Promise<BaseResponseDataModel<T[]>> {
    return this.send('POST', this.route(entity, 'getitems'), searchRequest, signal);
  }

  getAll<T = Row>(entity: string, signal?: AbortSignal): Promise<BaseResponseDataModel<T[]>> {
    return this.send('GET', this.route(entity, 'getall'), undefined, signal);
  }

  getById<T = Row>(entity: string, id: string | number, signal?: AbortSignal): Promise<BaseResponseDataModel<T>> {
    return this.send('GET', this.route(entity, `getbyid?id=${encodeURIComponent(String(id))}`), undefined, signal);
  }

  insert<T = Row>(entity: string, model: T, signal?: AbortSignal): Promise<BaseResponseDataModel<T>> {
    return this.send('POST', this.route(entity, 'insert'), model, signal);
  }

  update<T = Row>(entity: string, model: T, signal?: AbortSignal): Promise<BaseResponseDataModel<T>> {
    return this.send('POST', this.route(entity, 'update'), model, signal);
  }

  delete<T = Row>(entity: string, model: T, signal?: AbortSignal): Promise<BaseResponseDataModel<T>> {
    return this.send('POST', this.route(entity, 'delete'), model, signal);
  }

  /** Builds a search request (`BaseSearchRequestModel` wire format; `sortReverse: true` means ascending). */
  static searchRequest<TFilter = Row>(filter: TFilter, page: number, pageSize: number, sortProperty: string | null | undefined, ascending: boolean): SearchRequest<TFilter> {
    return {
      currentPage: page,
      requestItemSize: pageSize,
      sortProperty: sortProperty ?? null,
      sortReverse: ascending, // legacy wire semantics: true = ascending
      requestModel: filter,
    };
  }

  /** Route of an entity action; throws for invalid entity names. */
  protected route(entity: string, action: string): string {
    if (!ENTITY_NAME.test(entity)) {
      throw new Error(`Invalid entity name '${entity}'.`);
    }

    return `api/v${this.apiVersion}/${entity}/${action}`;
  }
}
