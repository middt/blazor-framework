import { computed, Directive, input, type Signal } from '@angular/core';
import type { UiNode, UiRenderContext } from '@middt/ui-core';

export type JsonObject = Record<string, unknown>;

export const isJsonObject = (value: unknown): value is JsonObject => typeof value === 'object' && value !== null && !Array.isArray(value);

/** Deep copy of a JSON value (`JsonNode.DeepClone`). */
export function cloneJson<T>(value: T): T {
  return value === undefined || value === null || typeof value !== 'object' ? value : (JSON.parse(JSON.stringify(value)) as T);
}

/** Property readers of a node (UiNode.StringProp/BoolProp/IntProp/ArrayProp/ObjectProp of the .NET model). */
export const UiProps = {
  string(node: UiNode, name: string): string | null {
    const value = node[name];
    return typeof value === 'string' ? value : null;
  },

  bool(node: UiNode, name: string, fallback = false): boolean {
    const value = node[name];
    return typeof value === 'boolean' ? value : fallback;
  },

  /** Integer property (a JSON number that fits an int); otherwise `fallback`. */
  int(node: UiNode, name: string, fallback = 0): number {
    const value = node[name];
    return typeof value === 'number' && Number.isInteger(value) && value >= -2147483648 && value <= 2147483647 ? value : fallback;
  },

  array(node: UiNode, name: string): unknown[] | null {
    const value = node[name];
    return Array.isArray(value) ? value : null;
  },

  object(node: UiNode, name: string): JsonObject | null {
    const value = node[name];
    return isJsonObject(value) ? value : null;
  },
} as const;

let counter = 0;

/**
 * Base class of widget components (counterpart of UiWidgetBase in the Blazor frontend): every widget receives the
 * node (its properties inline) and the render context as inputs.
 * <code>
 * @Component({ selector: 'app-chart-widget', template: `<div class="chart">{{ text('title') }}</div>` })
 * export class ChartWidget extends UiWidgetBase {}
 * </code>
 */
@Directive()
export abstract class UiWidgetBase {
  readonly node = input.required<UiNode>();
  readonly context = input.required<UiRenderContext>();

  #uniqueId: string | null = null;
  readonly #texts = new Map<string, Signal<string>>();

  /**
   * Renders the string property `name` as a template. Memoized per property/format: templates call this on every change
   * detection pass, but the text is only re-rendered when the node or the context changes.
   */
  protected text(name: string, format?: string | null): string {
    const key = format === null || format === undefined ? name : `${name}\u0000${format}`;
    let text = this.#texts.get(key);
    if (text === undefined) {
      text = computed(() => this.context().text(UiProps.string(this.node(), name), format));
      this.#texts.set(key, text);
    }

    return text();
  }

  protected stringProp(name: string): string | null {
    return UiProps.string(this.node(), name);
  }

  /** Element id: from the node id when set, otherwise unique per component instance (ids must not repeat on a page). */
  protected elementId(suffix?: string | null): string {
    const node = this.node();
    if (node.id !== null && node.id !== undefined) {
      return this.context().idFor(node, suffix);
    }

    this.#uniqueId ??= `${this.context().idPrefix}-${node.widget}${++counter}`;
    return suffix === null || suffix === undefined ? this.#uniqueId : `${this.#uniqueId}-${suffix}`;
  }
}
