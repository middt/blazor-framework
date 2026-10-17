import { inject, Injectable } from '@angular/core';
import { errorText, isSuccess } from '@middt/ui-core';
import { UI_DATA_CLIENT } from '@middt/angular/core';
import type { JsonObject } from './widget-base';

const DURATION_MS = 60_000;
const MAX_ITEMS = 500;

/** Per-session cache of lookup lists (`getall`) so several selects of one page share a request (UiLookupCache). */
@Injectable({ providedIn: 'root' })
export class UiLookupCache {
  readonly #client = inject(UI_DATA_CLIENT);
  readonly #entries = new Map<string, { loadedAt: number; items: Promise<JsonObject[]> }>();
  readonly #items = new Map<string, { loadedAt: number; item: Promise<JsonObject | null> }>();

  /** Rows of `entity`; rejects with the API's error text. */
  getAll(entity: string): Promise<JsonObject[]> {
    const now = Date.now();
    const entry = this.#entries.get(entity);
    if (entry !== undefined && now - entry.loadedAt < DURATION_MS) {
      return entry.items;
    }

    const items = this.#load(entity);
    this.#entries.set(entity, { loadedAt: now, items });
    return items;
  }

  /**
   * One row of `entity` by id (`getbyid`, e.g. the text of a stored picker value); null when it does not exist. Shared
   * by every editor of the session for {@link DURATION_MS}; rejects with the API's error text (failures are not cached).
   */
  getById(entity: string, id: string): Promise<JsonObject | null> {
    const cacheKey = `${entity}\u0000${id}`;
    const now = Date.now();
    const entry = this.#items.get(cacheKey);
    if (entry !== undefined && now - entry.loadedAt < DURATION_MS) {
      return entry.item;
    }

    const item = this.#loadItem(entity, id, cacheKey);
    this.#items.delete(cacheKey);
    if (this.#items.size >= MAX_ITEMS) {
      this.#items.delete(this.#items.keys().next().value as string);
    }

    this.#items.set(cacheKey, { loadedAt: now, item });
    return item;
  }

  async #loadItem(entity: string, id: string, cacheKey: string): Promise<JsonObject | null> {
    try {
      const response = await this.#client.getById<JsonObject>(entity, id);
      if (!isSuccess(response)) {
        throw new Error(errorText(response));
      }

      return response.data ?? null;
    } catch (error) {
      this.#items.delete(cacheKey);
      throw error;
    }
  }

  async #load(entity: string): Promise<JsonObject[]> {
    try {
      const response = await this.#client.getAll<JsonObject>(entity);
      if (!isSuccess(response)) {
        throw new Error(errorText(response));
      }

      return response.data ?? [];
    } catch (error) {
      this.#entries.delete(entity);
      throw error;
    }
  }
}
