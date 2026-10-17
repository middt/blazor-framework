import { ChangeDetectionStrategy, Component, computed, effect, inject, input, signal, untracked } from '@angular/core';
import { toSignal } from '@angular/core/rxjs-interop';
import { Title } from '@angular/platform-browser';
import { ActivatedRoute, type ParamMap } from '@angular/router';
import { errorText, isSuccess, UiExpressions, type UiLayoutDocument, type UiRenderContext } from '@middt/ui-core';
import { UiLayoutCache } from './layout-cache.service';
import { UiLayoutViewComponent } from './node.component';
import { UiRenderContextFactory } from './render-context';

/**
 * Routable host of a JSON layout page (UiPageHost): `{ path: 'ui/:key', component: MiddtLayoutPageComponent }` with
 * `withComponentInputBinding()`. Variables: `query` (query string values); sets the document title.
 */
@Component({
  selector: 'middt-layout-page',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [UiLayoutViewComponent],
  template: `
    @if (error(); as message) {
      <div class="middt-ui-alert middt-ui-alert-danger" role="alert">{{ message }}</div>
    } @else if (view(); as v) {
      @for (k of [v.renderKey]; track k) {
        <middt-ui-layout-view [layout]="v.layout" [context]="v.context" />
      }
    } @else {
      <p class="middt-ui-text middt-ui-text-muted" role="status">Yükleniyor...</p>
    }
  `,
})
export class MiddtLayoutPageComponent {
  readonly #layouts = inject(UiLayoutCache);
  readonly #contexts = inject(UiRenderContextFactory);
  readonly #title = inject(Title);
  readonly #query = toSignal(inject(ActivatedRoute).queryParamMap);
  #request = 0;

  readonly key = input.required<string>();

  protected readonly layout = signal<UiLayoutDocument | null>(null);
  protected readonly error = signal<string | null>(null);
  readonly #userReady = signal(false);

  protected readonly view = computed<{ layout: UiLayoutDocument; context: UiRenderContext; renderKey: string } | null>(() => {
    const layout = this.layout();
    if (layout === null || !this.#userReady()) {
      return null;
    }

    const params = this.#query();
    const query: Record<string, string> = {};
    for (const name of params?.keys ?? []) {
      query[name] = (params as ParamMap).getAll(name).join(',');
    }

    const queryKey = new URLSearchParams(query).toString();
    return {
      layout,
      context: this.#contexts.create({ [UiExpressions.query]: query }, `ui-${layout.key}`),
      renderKey: `${layout.key}:${layout.version ?? 0}:${queryKey}`,
    };
  });

  constructor() {
    void this.#contexts.ready().then(() => this.#userReady.set(true));
    effect(() => {
      const key = this.key();
      untracked(() => void this.#load(key));
    });
  }

  async #load(key: string): Promise<void> {
    const request = ++this.#request;
    this.error.set(null);
    if (this.layout()?.key === key) {
      return;
    }

    this.layout.set(null);
    const response = await this.#layouts.get(key);
    if (request !== this.#request) {
      return;
    }

    if (!isSuccess(response) || !response.data) {
      const text = errorText(response);
      this.error.set(text.length > 0 ? text : 'Sayfa bulunamadı.');
      return;
    }

    this.layout.set(response.data);
    this.#title.setTitle(response.data.title ?? response.data.key);
  }
}
