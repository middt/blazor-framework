import { ChangeDetectionStrategy, Component, computed, signal } from '@angular/core';
import { WorkflowTransitionViewBase } from '@middt/angular/workflow';
import { SAMPLE_VIEW_STYLES } from './view-format';

const OPTIONS: readonly { key: string; text: string }[] = [
  { key: 'stock', text: 'Stok yetersiz' },
  { key: 'price', text: 'Fiyat / iskonto uygun değil' },
  { key: 'risk', text: 'Müşteri riski yüksek' },
  { key: 'other', text: 'Diğer' },
];

/** Transition view of "reject": guided reason selection instead of a free text field. */
@Component({
  selector: 'app-reject-reason-form',
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <form class="sv-form" (submit)="$event.preventDefault(); onFormSubmit()">
      <fieldset>
        <legend>Red nedeni</legend>
        @for (option of options; track option.key) {
          <label class="sv-option" [for]="'sv-reason-' + option.key">
            <input type="radio" [id]="'sv-reason-' + option.key" name="sv-reason" [value]="option.key" [checked]="selected() === option.key"
                   (change)="selected.set(option.key)" [disabled]="busy()" />
            {{ option.text }}
          </label>
        }
      </fieldset>

      <label for="sv-reason-detail">Açıklama {{ selected() === 'other' ? '(zorunlu)' : '(isteğe bağlı)' }}</label>
      <textarea id="sv-reason-detail" rows="3" maxlength="400" [value]="detail()" (input)="detail.set($any($event.target).value)" [disabled]="busy()"></textarea>

      @if (error(); as error) {
        <p class="sv-error" role="alert">{{ error }}</p>
      }

      <div class="sv-actions">
        <button type="button" class="sv-button" (click)="cancel()" [disabled]="busy()">Vazgeç</button>
        <button type="submit" class="sv-button sv-danger" [disabled]="busy()">{{ busy() ? 'İşleniyor...' : 'Reddet' }}</button>
      </div>
    </form>
  `,
  styles: SAMPLE_VIEW_STYLES,
})
export class RejectReasonForm extends WorkflowTransitionViewBase {
  protected readonly options = OPTIONS;
  protected readonly selected = signal('stock');
  protected readonly detail = signal('');
  readonly #localError = signal<string | null>(null);
  protected readonly error = computed(() => this.#localError() ?? this.errors()[0] ?? null);

  protected onFormSubmit(): void {
    const selected = this.selected();
    const detail = this.detail().trim();
    this.#localError.set(selected === 'other' && detail === '' ? '"Diğer" için açıklama yazın.' : null);
    if (this.#localError() !== null) {
      return;
    }

    const text = OPTIONS.find(o => o.key === selected)!.text;
    this.submit({ reason: detail === '' ? text : `${text}: ${detail}` });
  }
}
