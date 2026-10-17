import { afterRenderEffect, ChangeDetectionStrategy, Component, computed, effect, ElementRef, inject, input, linkedSignal, output, signal, untracked, viewChildren, ViewEncapsulation } from '@angular/core';
import { ErrorStateMatcher } from '@angular/material/core';
import { MatCheckboxModule } from '@angular/material/checkbox';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInput, MatInputModule } from '@angular/material/input';
import { MatSelect, MatSelectModule } from '@angular/material/select';
import { schemaFields, schemaFormValue, schemaInitialValues, schemaLookupOptions, validateSchemaField, validateSchemaFields } from '@middt/ui-core';
import type { SchemaField, SchemaFieldValue, SchemaLookupOption, WorkflowData } from '@middt/ui-core';
import { UiLookupCache } from '@middt/angular/ui';
import { JSF_STYLES } from './workflow-styles';

/** Shown below an `x-lookup` select whose options cannot be loaded (same text as JsonSchemaForm.razor). */
export const LOOKUP_LOAD_ERROR = 'Liste yüklenemedi.';

/**
 * Input form of a JSON Schema subset (counterpart of JsonSchemaForm.razor): `type: object` with flat `properties` – string
 * (text, long text, enum, date/date-time, e-mail, pattern), number/integer and boolean; nested objects/arrays fall back to
 * a JSON text area. Properties with `x-lookup` (`{ entity, valueField, textField }`) are selects whose options are the rows of the
 * entity (loaded like the layout `select` lookups; the value type follows the property `type`). Call `validate()` before submitting to show the (Turkish) errors next to the fields and `getValue()` for
 * the body. The value is read when the schema changes (recreate the component to start over with another value).
 * Element ids: `{idPrefix}-{property}`; lookup field wrappers carry `data-lookup="{entity}"`.
 */
@Component({
  selector: 'middt-json-schema-form',
  imports: [MatFormFieldModule, MatInputModule, MatSelectModule, MatCheckboxModule],
  encapsulation: ViewEncapsulation.None,
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <div class="middt-jsf">
      @if (fields().length === 0 && emptyText()) {
        <p class="middt-jsf-empty">{{ emptyText() }}</p>
      }
      @for (field of fields(); track field.name) {
        @let id = idPrefix() + '-' + field.name;
        @let error = errors()[field.name];
        <div class="middt-jsf-field" [class.middt-jsf-invalid]="!!error" [attr.data-field]="field.name" [attr.data-lookup]="field.lookup?.entity ?? null">
          @if (field.kind === 'boolean') {
            <div class="middt-jsf-check">
              <mat-checkbox [id]="id" [checked]="values()[field.name] === true" [disabled]="disabled()"
                            [attr.aria-describedby]="error ? id + '-error' : null" (change)="set(field, $event.checked)">
                <span class="middt-jsf-label">{{ field.title }}</span>
              </mat-checkbox>
            </div>
          } @else {
            <label [for]="id" class="middt-jsf-label">
              {{ field.title }}
              @if (field.required) {
                <span class="middt-jsf-required" title="Zorunlu alan">*</span>
              }
            </label>
            <mat-form-field class="middt-jsf-input" subscriptSizing="dynamic">
              @if (field.lookup) {
                <mat-select [id]="id" [value]="text(field)" [disabled]="disabled()" placeholder="Seçiniz" [aria-label]="field.title"
                            [required]="field.required" [errorStateMatcher]="matcher(field)" (selectionChange)="set(field, $event.value ?? '')">
                  @if (!field.required) {
                    <mat-option value="">Seçiniz</mat-option>
                  }
                  @for (option of lookupOptions()[field.name]; track option.value) {
                    <mat-option [value]="option.value">{{ option.text }}</mat-option>
                  }
                </mat-select>
              } @else {
              @switch (field.kind) {
                @case ('longText') {
                  <textarea matInput [id]="id" rows="3" [value]="text(field)" [disabled]="disabled()" [attr.maxlength]="field.maxLength"
                            [attr.aria-required]="field.required ? 'true' : null" [errorStateMatcher]="matcher(field)" (input)="set(field, $any($event.target).value)"></textarea>
                }
                @case ('json') {
                  <textarea matInput [id]="id" rows="4" placeholder="JSON" class="middt-jsf-code" [value]="text(field)" [disabled]="disabled()"
                            [attr.aria-required]="field.required ? 'true' : null" [errorStateMatcher]="matcher(field)" (input)="set(field, $any($event.target).value)"></textarea>
                }
                @case ('enum') {
                  <mat-select [id]="id" [value]="text(field)" [disabled]="disabled()" placeholder="Seçiniz" [aria-label]="field.title"
                              [required]="field.required" [errorStateMatcher]="matcher(field)" (selectionChange)="set(field, $event.value ?? '')">
                    @if (!field.required) {
                      <mat-option value="">Seçiniz</mat-option>
                    }
                    @for (option of field.enumValues; track option) {
                      <mat-option [value]="option">{{ option }}</mat-option>
                    }
                  </mat-select>
                }
                @case ('date') {
                  <input matInput type="date" [id]="id" [value]="text(field)" [disabled]="disabled()" [attr.aria-required]="field.required ? 'true' : null"
                         [errorStateMatcher]="matcher(field)" (input)="set(field, $any($event.target).value)" (change)="set(field, $any($event.target).value)" />
                }
                @case ('dateTime') {
                  <input matInput type="datetime-local" [id]="id" [value]="text(field)" [disabled]="disabled()" [attr.aria-required]="field.required ? 'true' : null"
                         [errorStateMatcher]="matcher(field)" (input)="set(field, $any($event.target).value)" (change)="set(field, $any($event.target).value)" />
                }
                @case ('number') {
                  <input matInput type="number" step="any" inputmode="decimal" [id]="id" [value]="text(field)" [disabled]="disabled()"
                         [attr.min]="field.minimum" [attr.max]="field.maximum" [attr.aria-required]="field.required ? 'true' : null" [errorStateMatcher]="matcher(field)"
                         (input)="set(field, $any($event.target).value)" />
                }
                @case ('integer') {
                  <input matInput type="number" step="1" inputmode="numeric" [id]="id" [value]="text(field)" [disabled]="disabled()"
                         [attr.min]="field.minimum" [attr.max]="field.maximum" [attr.aria-required]="field.required ? 'true' : null" [errorStateMatcher]="matcher(field)"
                         (input)="set(field, $any($event.target).value)" />
                }
                @default {
                  <input matInput [type]="field.format === 'email' ? 'email' : 'text'" [id]="id" [value]="text(field)" [disabled]="disabled()"
                         [attr.maxlength]="field.maxLength" [attr.aria-required]="field.required ? 'true' : null" [errorStateMatcher]="matcher(field)"
                         [attr.autocomplete]="field.format === 'email' ? 'email' : null" (input)="set(field, $any($event.target).value)" />
                }
              }
              }
              @if (field.description) {
                <mat-hint class="middt-jsf-help">{{ field.description }}</mat-hint>
              }
              @if (error) {
                <mat-error [id]="id + '-error'" class="middt-jsf-error">{{ error }}</mat-error>
              }
            </mat-form-field>
          }
          @if (lookupErrors()[field.name]; as lookupError) {
            <div class="middt-jsf-error middt-jsf-lookup-error" role="alert">{{ lookupError }}</div>
          }
          @if (field.kind === 'boolean' && field.description) {
            <small class="middt-jsf-help">{{ field.description }}</small>
          }
          @if (field.kind === 'boolean' && error) {
            <div [id]="id + '-error'" class="middt-jsf-error" role="alert">{{ error }}</div>
          }
        </div>
      }
    </div>
  `,
  styles: JSF_STYLES,
})
export class MiddtJsonSchemaFormComponent {
  /** JSON Schema of the object to edit. */
  readonly schema = input<WorkflowData | null | undefined>(null);
  /** Initial value (read when the schema changes). */
  readonly value = input<WorkflowData | null | undefined>(null);
  /** Prefix of the element ids (unique per page when several forms are shown). */
  readonly idPrefix = input('jsf');
  readonly disabled = input(false);
  /** Text shown when the schema has no properties. */
  readonly emptyText = input<string | null>(null);
  /** Current value after every change. */
  readonly valueChange = output<WorkflowData>();

  readonly fields = computed<SchemaField[]>(() => schemaFields(this.schema()));

  /** `true` when the schema declares at least one property. */
  readonly hasFields = computed(() => this.fields().length > 0);

  protected readonly values = linkedSignal<SchemaField[], Record<string, SchemaFieldValue>>({
    source: this.fields,
    computation: fields => untracked(() => schemaInitialValues(this.schema(), fields, this.value())),
  });

  protected readonly errors = linkedSignal<SchemaField[], Record<string, string>>({ source: this.fields, computation: () => ({}) });

  /** Rows of the `x-lookup` entities as options, by field name. */
  readonly #lookupRows = linkedSignal<SchemaField[], Record<string, SchemaLookupOption[]>>({ source: this.fields, computation: () => ({}) });
  /** Loading errors of the `x-lookup` entities, by field name (shown below the select, like JsonSchemaForm.razor). */
  protected readonly lookupErrors = linkedSignal<SchemaField[], Record<string, string>>({ source: this.fields, computation: () => ({}) });

  /** Options of the lookup selects; a prefilled value missing from the loaded rows stays selectable (shown as is). */
  protected readonly lookupOptions = computed<Record<string, SchemaLookupOption[]>>(() => {
    const rows = this.#lookupRows();
    const values = this.values();
    const result: Record<string, SchemaLookupOption[]> = {};
    for (const field of this.fields()) {
      if (field.lookup === null) {
        continue;
      }

      const options = rows[field.name] ?? [];
      const value = values[field.name];
      result[field.name] = typeof value === 'string' && value !== '' && !options.some(o => o.value === value)
        ? [{ value, text: value }, ...options]
        : options;
    }

    return result;
  });

  readonly #validated = signal(false);
  readonly #matchers = new Map<string, ErrorStateMatcher>();
  readonly #host = inject<ElementRef<HTMLElement>>(ElementRef);
  readonly #lookupCache = inject(UiLookupCache);
  private readonly inputs = viewChildren(MatInput);
  private readonly selects = viewChildren(MatSelect);

  constructor() {
    // The inputs are not bound to form controls: refresh their error state (red outline, mat-error, aria-invalid) explicitly.
    afterRenderEffect(() => {
      this.errors();
      for (const control of [...this.inputs(), ...this.selects()]) {
        control.updateErrorState();
      }
    });
    effect(() => {
      const fields = this.fields();
      untracked(() => void this.#loadLookups(fields));
    });
  }

  /** Validates all fields, shows the errors and returns `true` when the value is valid. */
  validate(): boolean {
    this.#validated.set(true);
    const errors = validateSchemaFields(this.fields(), this.values());
    this.errors.set(errors);
    return Object.keys(errors).length === 0;
  }

  /** Current value (empty optional fields are omitted). */
  getValue(): WorkflowData {
    return schemaFormValue(this.fields(), this.values());
  }

  /** Clears the entered values and errors (back to `value`, else the schema defaults). */
  reset(value: WorkflowData | null = null): void {
    this.#validated.set(false);
    this.values.set(schemaInitialValues(this.schema(), this.fields(), value));
    this.errors.set({});
  }

  /** Moves the keyboard focus to the first field (e.g. when a wizard step form is shown). */
  focusFirst(): void {
    const first = this.fields()[0];
    if (first === undefined) {
      return;
    }

    const element = this.#host.nativeElement.querySelector<HTMLElement>(`[data-field="${CSS.escape(first.name)}"] :is(input, textarea, .mat-mdc-select)`);
    element?.focus();
  }

  /** Loads the options of every `x-lookup` field (rows cached per session by UiLookupCache, shared with the layout selects). */
  async #loadLookups(fields: readonly SchemaField[]): Promise<void> {
    await Promise.all(fields.map(async field => {
      const lookup = field.lookup;
      if (lookup === null) {
        return;
      }

      try {
        const rows = await this.#lookupCache.getAll(lookup.entity);
        if (this.fields() === fields) {
          this.#lookupRows.update(all => ({ ...all, [field.name]: schemaLookupOptions(lookup, rows) }));
          this.lookupErrors.update(all => {
            const next = { ...all };
            delete next[field.name];
            return next;
          });
        }
      } catch {
        if (this.fields() === fields) {
          this.lookupErrors.update(all => ({ ...all, [field.name]: LOOKUP_LOAD_ERROR }));
        }
      }
    }));
  }

  protected text(field: SchemaField): string {
    const value = this.values()[field.name];
    return typeof value === 'string' ? value : '';
  }

  protected set(field: SchemaField, value: SchemaFieldValue): void {
    this.values.update(values => ({ ...values, [field.name]: value }));
    if (this.#validated()) {
      const error = validateSchemaField(field, value);
      this.errors.update(errors => {
        const next = { ...errors };
        if (error === null) {
          delete next[field.name];
        } else {
          next[field.name] = error;
        }

        return next;
      });
    }

    this.valueChange.emit(this.getValue());
  }

  /** Shows a field as invalid while it has a validation error. */
  protected matcher(field: SchemaField): ErrorStateMatcher {
    let matcher = this.#matchers.get(field.name);
    if (matcher === undefined) {
      matcher = { isErrorState: () => this.errors()[field.name] !== undefined };
      this.#matchers.set(field.name, matcher);
    }

    return matcher;
  }
}
