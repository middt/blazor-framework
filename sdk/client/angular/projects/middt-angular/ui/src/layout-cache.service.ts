import { inject, Injectable } from '@angular/core';
import { isSuccess, ResultEnum, type BaseResponseDataModel, type UiLayoutDocument } from '@middt/ui-core';
import { UI_API_CLIENT } from '@middt/angular/core';

/** How long a loaded layout is reused without asking the server again. */
export const LAYOUT_CACHE_TTL_MS = 30_000;
const MAX_ENTRIES = 100;

interface Entry {
  loadedAt: number;
  response: Promise<BaseResponseDataModel<UiLayoutDocument>>;
}

/**
 * Short lived client cache of layouts (`GET api/ui/layouts/{key}`) so navigating between layout pages does not re-fetch
 * the same document. Only successful responses are kept; the runtime editor invalidates a key when it saves or deletes it.
 */
@Injectable({ providedIn: 'root' })
export class UiLayoutCache {
  readonly #client = inject(UI_API_CLIENT);
  readonly #entries = new Map<string, Entry>();

  /** The layout `key` (concurrent callers share one request); never throws, failures come back as `result: -1`. */
  get(key: string): Promise<BaseResponseDataModel<UiLayoutDocument>> {
    const now = Date.now();
    const entry = this.#entries.get(key);
    if (entry !== undefined && now - entry.loadedAt < LAYOUT_CACHE_TTL_MS) {
      return entry.response;
    }

    const response = this.#load(key);
    this.#entries.delete(key);
    if (this.#entries.size >= MAX_ENTRIES) {
      this.#entries.delete(this.#entries.keys().next().value as string);
    }

    this.#entries.set(key, { loadedAt: now, response });
    void response.then(result => {
      // Failures (not found, forbidden, network) are not cached.
      if ((!isSuccess(result) || !result.data) && this.#entries.get(key)?.response === response) {
        this.#entries.delete(key);
      }
    });
    return response;
  }

  /** Drops `key` (or every layout) so the next request loads it from the server. */
  invalidate(key?: string | null): void {
    if (key === undefined || key === null) {
      this.#entries.clear();
    } else {
      this.#entries.delete(key);
    }
  }

  async #load(key: string): Promise<BaseResponseDataModel<UiLayoutDocument>> {
    try {
      return await this.#client.get(key);
    } catch (error) {
      // Invalid keys (e.g. '..') are rejected by the client before a request is made.
      return { result: ResultEnum.error, messageList: [error instanceof Error ? error.message : String(error)], count: 0, data: null };
    }
  }
}
