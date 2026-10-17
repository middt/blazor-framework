import { NgTemplateOutlet } from '@angular/common';
import { afterRenderEffect, ChangeDetectionStrategy, Component, computed, effect, inject, Injectable, input, output, signal, untracked, viewChildren } from '@angular/core';
import { MatButtonModule } from '@angular/material/button';
import { MatCheckboxModule } from '@angular/material/checkbox';
import { DateAdapter, ErrorStateMatcher, MAT_DATE_FORMATS, NativeDateAdapter } from '@angular/material/core';
import { MatDatepickerModule } from '@angular/material/datepicker';
import { MatDialog } from '@angular/material/dialog';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatIconModule } from '@angular/material/icon';
import { MatInput, MatInputModule } from '@angular/material/input';
import { MatSelect, MatSelectModule } from '@angular/material/select';
import { UiField, UiFieldJson, UiOption, type UiRenderContext } from '@middt/ui-core';
import { UiPickerDialogComponent, type UiPickerDialogData } from './dialogs';
import { UiLookupCache } from './lookup-cache.service';
import { cloneJson, type JsonObject } from './widget-base';

const pad = (n: number, length = 2): string => String(n).padStart(length, '0');

/** Date input as dd.MM.yyyy (like the Radzen date picker of the Blazor frontend). */
@Injectable()
export class UiDateAdapter extends NativeDateAdapter {
  override parse(value: unknown, parseFormat?: unknown): Date | null {
    if (typeof value === 'string') {
      const match = /^\s*(\d{1,2})[./-](\d{1,2})[./-](\d{4})\s*$/.exec(value);
      if (match !== null) {
        const [, d, m, y] = match;
        const date = new Date(Number(y), Number(m) - 1, Number(d));
        return date.getDate() === Number(d) && date.getMonth() === Number(m) - 1 ? date : this.invalid();
      }
    }

    return super.parse(value, parseFormat);
  }

  override format(date: Date, displayFormat: object): string {
    return (displayFormat as unknown) === 'input'
      ? `${pad(date.getDate())}.${pad(date.getMonth() + 1)}.${pad(date.getFullYear(), 4)}`
      : super.format(date, displayFormat);
  }
}

const UI_DATE_FORMATS = {
  parse: { dateInput: 'input' },
  display: {
    dateInput: 'input',
    monthYearLabel: { year: 'numeric', month: 'short' },
    dateA11yLabel: { year: 'numeric', month: 'long', day: 'numeric' },
    monthYearA11yLabel: { year: 'numeric', month: 'long' },
  },
};

const ISO_DATE = /^(\d{4})-(\d{2})-(\d{2})(?:[T ](\d{2}):(\d{2})(?::(\d{2})(?:\.\d+)?)?)?(Z|[+-]\d{2}:?\d{2})?$/i;

/** Stored date text → local Date (UTC/offset values are converted to local time, unzoned values keep their wall clock). */
function parseStoredDate(value: unknown): Date | null {
  if (typeof value !== 'string') {
    return null;
  }

  const match = ISO_DATE.exec(value.trim());
  if (match === null) {
    return null;
  }

  const [, y, mo, d, h = '0', mi = '0', s = '0', zone] = match;
  const date = zone === undefined ? new Date(Number(y), Number(mo) - 1, Number(d), Number(h), Number(mi), Number(s)) : new Date(value.trim());
  return Number.isNaN(date.getTime()) ? null : date;
}

const MAX_UNFILTERED_OPTIONS = 10;

/**
 * Editor of one form/search field (text, textarea, number, integer, date, datetime, checkbox, select, picker); writes
 * the value to `target[field]` (removed when cleared) and raises `changed`.
 */
@Component({
  selector: 'middt-ui-field-editor',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [NgTemplateOutlet, MatFormFieldModule, MatInputModule, MatCheckboxModule, MatSelectModule, MatDatepickerModule, MatButtonModule, MatIconModule],
  providers: [
    { provide: DateAdapter, useClass: UiDateAdapter },
    { provide: MAT_DATE_FORMATS, useValue: UI_DATE_FORMATS },
  ],
  host: { style: 'display: contents' },
  template: `
    <div class="middt-ui-input" [class.middt-ui-invalid]="error()" [attr.data-field]="field().field">
      @if (field().type === 'checkbox') {
        <div class="middt-ui-check">
          <mat-checkbox [id]="id()" [checked]="boolValue()" (change)="set($event.checked)" [disabled]="disabled()">{{ field().label }}</mat-checkbox>
        </div>
      } @else {
        <label [for]="id()">
          {{ field().label }}
          @if (field().required) {
            <span class="middt-jsf-required" title="Zorunlu alan">*</span>
          }
        </label>
        @switch (field().type) {
          @case ('textarea') {
            <mat-form-field appearance="outline" subscriptSizing="dynamic">
              <textarea matInput [id]="id()" [value]="textValue()" (input)="setText(eventValue($event))" [disabled]="disabled()" rows="3"
                        [attr.maxlength]="field().maxLength" [placeholder]="field().placeholder ?? ''" [errorStateMatcher]="matcher"
                        [aria-describedby]="describedBy()"></textarea>
            </mat-form-field>
          }
          @case ('number') {
            <ng-container *ngTemplateOutlet="number" />
          }
          @case ('integer') {
            <ng-container *ngTemplateOutlet="number" />
          }
          @case ('date') {
            <mat-form-field appearance="outline" subscriptSizing="dynamic">
              <input matInput [id]="id()" [matDatepicker]="picker" [value]="dateValue()" (dateChange)="setDate($event.value)" [disabled]="disabled()"
                     placeholder="gg.aa.yyyy" [errorStateMatcher]="matcher" [aria-describedby]="describedBy()" />
              <mat-datepicker-toggle matIconSuffix [for]="picker" />
              <mat-datepicker #picker />
            </mat-form-field>
          }
          @case ('datetime') {
            <mat-form-field appearance="outline" subscriptSizing="dynamic">
              <input matInput type="datetime-local" [id]="id()" [value]="dateTimeValue()" (change)="setDateTime(eventValue($event))" [disabled]="disabled()"
                     [errorStateMatcher]="matcher" [aria-describedby]="describedBy()" />
            </mat-form-field>
          }
          @case ('select') {
            <mat-form-field appearance="outline" subscriptSizing="dynamic">
              <mat-select [id]="id()" [value]="selectedKey()" (selectionChange)="setOption($event.value)" [disabled]="disabled()"
                          [placeholder]="field().placeholder ?? loadError() ?? 'Seçiniz'" [aria-label]="field().label" [errorStateMatcher]="matcher"
                          [aria-describedby]="describedBy()" (openedChange)="filter.set('')">
                @if (options().length > maxUnfiltered) {
                  <div class="middt-ui-select-filter">
                    <input type="search" placeholder="Ara" [attr.aria-label]="field().label + ' filtrele'" [value]="filter()"
                           (input)="filter.set(eventValue($event))" (keydown)="$event.stopPropagation()" />
                  </div>
                }
                @for (option of filteredOptions(); track option.key) {
                  <mat-option [value]="option.key">{{ option.text }}</mat-option>
                }
              </mat-select>
              @if (hasValue() && !disabled()) {
                <button mat-icon-button matIconSuffix type="button" title="Temizle" [attr.aria-label]="field().label + ' temizle'"
                        (click)="$event.stopPropagation(); set(null)"><mat-icon>close</mat-icon></button>
              }
            </mat-form-field>
          }
          @case ('picker') {
            <div class="middt-ui-picker">
              <mat-form-field appearance="outline" subscriptSizing="dynamic">
                <input matInput [id]="id()" [value]="pickerText() ?? ''" readonly [placeholder]="field().placeholder ?? 'Seçiniz'"
                       [errorStateMatcher]="matcher" [aria-describedby]="describedBy()" />
              </mat-form-field>
              <button mat-icon-button type="button" (click)="pick()" [disabled]="disabled()" title="Seç" [attr.aria-label]="field().label + ' seç'">
                <mat-icon>search</mat-icon>
              </button>
              @if (hasValue() && !disabled()) {
                <button mat-icon-button type="button" (click)="clearPick()" title="Temizle" [attr.aria-label]="field().label + ' temizle'">
                  <mat-icon>close</mat-icon>
                </button>
              }
            </div>
          }
          @default {
            <mat-form-field appearance="outline" subscriptSizing="dynamic">
              <input matInput [id]="id()" [value]="textValue()" (input)="setText(eventValue($event))" [disabled]="disabled()"
                     [attr.maxlength]="field().maxLength" [placeholder]="field().placeholder ?? ''" [errorStateMatcher]="matcher"
                     [aria-describedby]="describedBy()" />
            </mat-form-field>
          }
        }
      }
      @if (error()) {
        <div [id]="errorId()" class="middt-ui-input-error" role="alert">{{ error() }}</div>
      }
    </div>

    <ng-template #number>
      <mat-form-field appearance="outline" subscriptSizing="dynamic">
        <input matInput type="number" [id]="id()" [value]="numberValue()" (input)="setNumber(eventValue($event))" [disabled]="disabled()"
               [step]="field().type === 'integer' ? 1 : 'any'" [placeholder]="field().placeholder ?? ''" [errorStateMatcher]="matcher"
               [aria-describedby]="describedBy()" />
      </mat-form-field>
    </ng-template>
  `,
  styles: `
    .middt-ui-input mat-form-field { width: 100%; }
    .middt-ui-picker mat-form-field { flex: 1; }
    .middt-ui-picker { align-items: center; }
    .middt-ui-select-filter { position: sticky; top: 0; z-index: 1; padding: .5rem; background: var(--mat-sys-surface-container, #fff); }
    .middt-ui-select-filter input { width: 100%; box-sizing: border-box; padding: .4rem .5rem; font: inherit; color: inherit; background: transparent;
      border: 1px solid var(--mat-sys-outline-variant, #ccc); border-radius: 4px; }
    .middt-jsf-required { color: var(--mat-sys-error, #dc3545); }
  `,
})
export class UiFieldEditorComponent {
  readonly #lookupCache = inject(UiLookupCache);
  readonly #dialog = inject(MatDialog);

  readonly field = input.required<UiField>();
  /** Object the value is written to (`target[field.field]`). */
  readonly target = input.required<JsonObject>();
  readonly context = input.required<UiRenderContext>();
  readonly idPrefix = input('ui');
  readonly disabled = input(false);
  readonly error = input<string | null | undefined>(null);
  readonly changed = output<void>();

  protected readonly maxUnfiltered = MAX_UNFILTERED_OPTIONS;
  /** Bumped on every write: the target object is mutated in place. */
  readonly #revision = signal(0);
  protected readonly filter = signal('');
  protected readonly options = signal<UiOption[]>([]);
  protected readonly loadError = signal<string | null>(null);
  protected readonly pickerText = signal<string | null>(null);
  #pickerValueKey: string | null = null;

  protected readonly matcher: ErrorStateMatcher = { isErrorState: () => !!this.error() };
  private readonly inputs = viewChildren(MatInput);
  private readonly selects = viewChildren(MatSelect);

  protected readonly id = computed(() => `${this.idPrefix()}-${this.field().field}`);
  protected readonly errorId = computed(() => `${this.id()}-error`);
  protected readonly describedBy = computed(() => (this.error() ? this.errorId() : ''));

  protected readonly value = computed<unknown>(() => {
    this.#revision();
    const target = this.target();
    const name = this.field().field;
    return Object.prototype.hasOwnProperty.call(target, name) ? target[name] : undefined;
  });

  protected readonly hasValue = computed(() => this.value() !== null && this.value() !== undefined);
  protected readonly textValue = computed(() => {
    const value = this.value();
    return value === null || value === undefined ? '' : typeof value === 'string' ? value : JSON.stringify(value);
  });
  protected readonly numberValue = computed(() => {
    const number = UiFieldJson.toDecimal(this.value());
    return number === null ? '' : String(number);
  });
  protected readonly boolValue = computed(() => this.value() === true);
  protected readonly dateValue = computed(() => parseStoredDate(this.value()));
  protected readonly dateTimeValue = computed(() => {
    const date = this.dateValue();
    return date === null
      ? ''
      : `${pad(date.getFullYear(), 4)}-${pad(date.getMonth() + 1)}-${pad(date.getDate())}T${pad(date.getHours())}:${pad(date.getMinutes())}`;
  });
  protected readonly selectedKey = computed(() => (this.hasValue() ? UiFieldJson.key(this.value()) : null));
  protected readonly filteredOptions = computed(() => {
    const options = this.options();
    const filter = this.filter().trim().toLocaleLowerCase('tr-TR');
    if (options.length <= MAX_UNFILTERED_OPTIONS || filter === '') {
      return options;
    }

    const selected = this.selectedKey();
    return options.filter(o => o.key === selected || o.text.toLocaleLowerCase('tr-TR').includes(filter));
  });

  constructor() {
    // The controls are not bound to form controls: refresh their error state (red outline, aria-invalid) explicitly.
    afterRenderEffect(() => {
      this.error();
      for (const control of [...this.inputs(), ...this.selects()]) {
        control.updateErrorState();
      }
    });

    // Options of a select: static, or loaded from the lookup entity (cached per session).
    effect(() => {
      const field = this.field();
      const context = this.context();
      untracked(() => void this.#loadOptions(field, context));
    });

    // Picker: show the text of a stored value (editing an existing record).
    effect(() => {
      const field = this.field();
      const context = this.context();
      const value = this.value();
      untracked(() => {
        if (field.type !== 'picker') {
          return;
        }

        if (value === null || value === undefined) {
          this.#pickerValueKey = null;
          this.pickerText.set(null);
        } else if (field.lookup !== null && context.interactive) {
          const valueKey = UiFieldJson.key(value);
          if (this.pickerText() === null || this.#pickerValueKey !== valueKey) {
            this.#pickerValueKey = valueKey;
            void this.#loadPickerText(field, value);
          }
        }
      });
    });
  }

  protected eventValue(event: Event): string {
    return (event.target as HTMLInputElement).value;
  }

  protected set(value: unknown): void {
    const target = this.target();
    const name = this.field().field;
    if (value === null || value === undefined) {
      delete target[name];
    } else {
      target[name] = value;
    }

    this.#revision.update(r => r + 1);
    this.changed.emit();
  }

  protected setText(value: string): void {
    this.set(value === '' ? null : value);
  }

  protected setNumber(text: string): void {
    const number = text.trim() === '' ? null : Number(text);
    this.set(number === null || !Number.isFinite(number) ? null : number);
  }

  protected setDate(date: Date | null): void {
    this.set(date === null || Number.isNaN(date.getTime()) ? null : `${pad(date.getFullYear(), 4)}-${pad(date.getMonth() + 1)}-${pad(date.getDate())}`);
  }

  protected setDateTime(text: string): void {
    // datetime-local values are local wall clock times; stored as ISO UTC.
    const date = text === '' ? null : new Date(text);
    this.set(date === null || Number.isNaN(date.getTime()) ? null : date.toISOString());
  }

  protected setOption(key: string | null): void {
    this.set(key === null ? null : cloneJson(this.options().find(o => o.key === key)?.value ?? null));
  }

  protected pick(): void {
    const field = this.field();
    const lookup = field.lookup;
    if (lookup === null) {
      return;
    }

    const data: UiPickerDialogData = { lookup, title: lookup.title ?? field.label, context: this.context() };
    this.#dialog
      .open<UiPickerDialogComponent, UiPickerDialogData, JsonObject | undefined>(UiPickerDialogComponent, {
        data,
        width: 'min(960px, 95vw)',
        maxWidth: '95vw',
        autoFocus: 'dialog',
      })
      .afterClosed()
      .subscribe(row => {
        if (row) {
          const pickedValue = cloneJson(row[lookup.valueField]);
          this.#pickerValueKey = pickedValue === null || pickedValue === undefined ? null : UiFieldJson.key(pickedValue);
          this.pickerText.set(UiFieldJson.key(row[lookup.textField]));
          this.set(pickedValue);
        }
      });
  }

  protected clearPick(): void {
    this.#pickerValueKey = null;
    this.pickerText.set(null);
    this.set(null);
  }

  async #loadOptions(field: UiField, context: UiRenderContext): Promise<void> {
    if (field.type !== 'select') {
      return;
    }

    this.options.set(field.options);
    const lookup = field.lookup;
    if (lookup === null || !context.interactive) {
      return;
    }

    try {
      const items = await this.#lookupCache.getAll(lookup.entity);
      if (this.field() === field) {
        this.options.set(items.map(i => new UiOption(cloneJson(i[lookup.valueField] ?? null), UiFieldJson.key(i[lookup.textField]))));
        this.loadError.set(null);
      }
    } catch (error) {
      this.loadError.set(error instanceof Error ? error.message : String(error));
    }
  }

  async #loadPickerText(field: UiField, value: unknown): Promise<void> {
    const lookup = field.lookup!;
    const key = UiFieldJson.key(value);
    try {
      const item = await this.#lookupCache.getById(lookup.entity, key);
      if (UiFieldJson.key(this.value()) === key) {
        this.pickerText.set(item ? UiFieldJson.key(item[lookup.textField]) : key);
      }
    } catch {
      if (UiFieldJson.key(this.value()) === key) {
        this.pickerText.set(key);
      }
    }
  }
}
