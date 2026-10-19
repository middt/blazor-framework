import { ChangeDetectionStrategy, Component, computed, effect, forwardRef, inject, signal, untracked } from '@angular/core';
import { errorText, isSuccess, PortableExpressionError, UiExpressions, type UiLayoutDocument } from '@middt/ui-core';
import { UiLayoutCache } from './layout-cache.service';
import { UiNodeListComponent } from './node.component';
import { UiProps, UiWidgetBase } from './widget-base';

const MAX_DEPTH = 5;
const DEPTH_KEY = '__layoutDepth';

/** `layout` widget: embeds a reusable view layout by `key` (optionally scoping `data` to a sub-expression). */
@Component({
  selector: 'middt-ui-layout-widget',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [forwardRef(() => UiNodeListComponent)],
  host: { style: 'display: contents' },
  template: `
    @if (loading()) {
      <p class="middt-ui-text middt-ui-text-muted" role="status">Yükleniyor...</p>
    } @else if (error(); as message) {
      <div class="middt-ui-alert middt-ui-alert-warning" role="alert">{{ message }}</div>
    } @else if (layout(); as l) {
      @if (childContext(); as c) {
        <middt-ui-node-list [nodes]="l.body" [context]="c" />
      }
    }
  `,
})
export class UiLayoutWidget extends UiWidgetBase {
  readonly #layouts = inject(UiLayoutCache);
  #loadedKey: string | null = null;
  #request = 0;

  protected readonly loading = signal(true);
  protected readonly layout = signal<UiLayoutDocument | null>(null);
  protected readonly error = signal<string | null>(null);
  protected readonly childContext = computed(() => {
    const parent = this.context();
    const depth = typeof parent.get(DEPTH_KEY) === 'number' ? (parent.get(DEPTH_KEY) as number) : 0;
    if (depth >= MAX_DEPTH) {
      return null;
    }

    let next = parent.with(DEPTH_KEY, depth + 1);
    const dataExpr = UiProps.string(this.node(), 'data');
    if (dataExpr && dataExpr.trim() !== '') {
      try {
        next = next.with(UiExpressions.data, parent.evaluate(dataExpr));
      } catch (err) {
        if (err instanceof PortableExpressionError) {
          next = next.with(UiExpressions.data, null);
        } else {
          throw err;
        }
      }
    }

    return next;
  });

  constructor() {
    super();
    effect(() => {
      const key = UiProps.string(this.node(), 'key') ?? '';
      const depth = typeof this.context().get(DEPTH_KEY) === 'number' ? (this.context().get(DEPTH_KEY) as number) : 0;
      untracked(() => void this.#load(key, depth));
    });
  }

  async #load(key: string, depth: number): Promise<void> {
    if (depth >= MAX_DEPTH) {
      this.loading.set(false);
      this.error.set(`Alt yerleşim derinlik sınırı (${MAX_DEPTH}) aşıldı.`);
      return;
    }

    if (key === this.#loadedKey) {
      return;
    }

    this.#loadedKey = key;
    const request = ++this.#request;
    this.layout.set(null);
    this.error.set(null);
    if (key.trim() === '') {
      this.loading.set(false);
      return;
    }

    this.loading.set(true);
    try {
      const response = await this.#layouts.get(key);
      if (request === this.#request) {
        const doc = isSuccess(response) ? (response.data ?? null) : null;
        this.layout.set(doc);
        this.error.set(doc === null ? `'${key}' yerleşimi yüklenemedi: ${errorText(response)}` : null);
      }
    } finally {
      if (request === this.#request) {
        this.loading.set(false);
      }
    }
  }
}
