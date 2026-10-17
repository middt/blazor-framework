import { ChangeDetectionStrategy, Component, computed, effect, inject, input, signal, untracked } from '@angular/core';
import { errorText, isSuccess, type UiLayoutDocument } from '@middt/ui-core';
import { UiLayoutCache } from './layout-cache.service';
import { UiLayoutViewComponent } from './node.component';
import { UiRenderContextFactory } from './render-context';

/**
 * Embeds a JSON layout (kind "view") with the given variables (UiLayoutHost), e.g. a workflow state view:
 * <code>&lt;middt-layout-host layoutKey="order-review" [variables]="{ data, instance }" /&gt;</code>
 */
@Component({
  selector: 'middt-layout-host',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [UiLayoutViewComponent],
  template: `
    @if (error(); as message) {
      <div class="middt-ui-alert middt-ui-alert-warning" role="alert">{{ message }}</div>
    } @else if (layout(); as l) {
      @if (context(); as c) {
        <middt-ui-layout-view [layout]="l" [context]="c" />
      }
    }
  `,
})
export class MiddtLayoutHostComponent {
  readonly #layouts = inject(UiLayoutCache);
  readonly #contexts = inject(UiRenderContextFactory);
  #request = 0;

  readonly layoutKey = input.required<string>();
  /** Expression variables (`data`, `instance`, ...). */
  readonly variables = input<Record<string, unknown>>({});
  readonly idPrefix = input('ui-view');

  protected readonly layout = signal<UiLayoutDocument | null>(null);
  protected readonly error = signal<string | null>(null);
  readonly #userReady = signal(false);

  protected readonly context = computed(() => (this.#userReady() ? this.#contexts.create(this.variables() ?? {}, this.idPrefix()) : null));

  constructor() {
    void this.#contexts.ready().then(() => this.#userReady.set(true));
    effect(() => {
      const key = this.layoutKey();
      untracked(() => void this.#load(key));
    });
  }

  async #load(key: string): Promise<void> {
    if (this.layout()?.key === key) {
      return;
    }

    const request = ++this.#request;
    const response = await this.#layouts.get(key);
    if (request !== this.#request) {
      return;
    }

    const layout = isSuccess(response) ? (response.data ?? null) : null;
    this.layout.set(layout);
    this.error.set(layout === null ? `'${key}' yerleşimi yüklenemedi: ${errorText(response)}` : null);
  }
}
