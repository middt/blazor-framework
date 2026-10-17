// Dialogs of the layout widgets: confirmation, create/edit form (UiFormDialog) and lookup picker (UiPickerDialog).

import { ChangeDetectionStrategy, Component, computed, forwardRef, inject, signal } from '@angular/core';
import { MatButtonModule } from '@angular/material/button';
import { MAT_DIALOG_DATA, MatDialog, MatDialogModule, MatDialogRef } from '@angular/material/dialog';
import { MatIconModule } from '@angular/material/icon';
import { UiColumn, type UiField, type UiLookup, type UiRenderContext } from '@middt/ui-core';
import { firstValueFrom } from 'rxjs';
import { UiDataGridComponent } from './data-grid.component';
import { UiFieldEditorComponent } from './field-editor.component';
import type { JsonObject } from './widget-base';

export interface UiConfirmDialogData {
  title: string;
  message: string;
  ok?: string;
  cancel?: string;
}

/** Confirmation dialog (Radzen DialogService.Confirm); closes with `true` when confirmed. */
@Component({
  selector: 'middt-ui-confirm-dialog',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [MatDialogModule, MatButtonModule],
  template: `
    <h2 mat-dialog-title>{{ data.title }}</h2>
    <mat-dialog-content><p class="middt-ui-text">{{ data.message }}</p></mat-dialog-content>
    <mat-dialog-actions align="end">
      <button mat-button type="button" [mat-dialog-close]="false">{{ data.cancel ?? 'Vazgeç' }}</button>
      <button mat-flat-button type="button" [mat-dialog-close]="true" cdkFocusInitial>{{ data.ok ?? 'Tamam' }}</button>
    </mat-dialog-actions>
  `,
})
export class UiConfirmDialogComponent {
  protected readonly data = inject<UiConfirmDialogData>(MAT_DIALOG_DATA);
}

/** Opens {@link UiConfirmDialogComponent}; resolves to true when confirmed. */
export async function confirmDialog(dialog: MatDialog, data: UiConfirmDialogData): Promise<boolean> {
  const ref = dialog.open<UiConfirmDialogComponent, UiConfirmDialogData, boolean>(UiConfirmDialogComponent, { data, width: 'min(420px, 95vw)', role: 'alertdialog' });
  return (await firstValueFrom(ref.afterClosed())) === true;
}

export interface UiFormDialogData {
  title: string;
  fields: UiField[];
  /** Copy of the record being edited (new object for create). */
  model: JsonObject;
  context: UiRenderContext;
  /** Saves the model; returns an error message, or null on success (the dialog then closes with the model). */
  save: (model: JsonObject) => Promise<string | null>;
}

/** Create/edit form of a grid: validation (Turkish messages), server error shown in the dialog. */
@Component({
  selector: 'middt-ui-form-dialog',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [MatDialogModule, MatButtonModule, forwardRef(() => UiFieldEditorComponent)],
  template: `
    <h2 mat-dialog-title>{{ data.title }}</h2>
    <form class="middt-ui-form" novalidate (submit)="$event.preventDefault(); submit()">
      <mat-dialog-content>
        <div class="middt-ui-form">
          @for (field of data.fields; track field.field) {
            <middt-ui-field-editor [field]="field" [target]="data.model" [context]="data.context" idPrefix="ui-form" [disabled]="busy()"
                                   [error]="errors()[field.field] ?? null" (changed)="revalidate(field)" />
          }
          @if (serverError()) {
            <div class="middt-ui-alert middt-ui-alert-danger" role="alert">{{ serverError() }}</div>
          }
        </div>
      </mat-dialog-content>
      <mat-dialog-actions>
        <div class="middt-ui-toolbar">
          <button mat-flat-button type="submit" [disabled]="busy()">{{ busy() ? 'Kaydediliyor...' : 'Kaydet' }}</button>
          <button mat-stroked-button type="button" (click)="ref.close()" [disabled]="busy()">İptal</button>
        </div>
      </mat-dialog-actions>
    </form>
  `,
})
export class UiFormDialogComponent {
  protected readonly data = inject<UiFormDialogData>(MAT_DIALOG_DATA);
  protected readonly ref = inject<MatDialogRef<UiFormDialogComponent, JsonObject>>(MatDialogRef);
  protected readonly errors = signal<Record<string, string>>({});
  protected readonly busy = signal(false);
  protected readonly serverError = signal<string | null>(null);
  #validated = false;

  protected revalidate(field: UiField): void {
    if (!this.#validated) {
      return;
    }

    const error = field.validate(this.data.model, this.data.context.locale);
    this.errors.update(errors => {
      const next = { ...errors };
      if (error === null) {
        delete next[field.field];
      } else {
        next[field.field] = error;
      }

      return next;
    });
  }

  protected async submit(): Promise<void> {
    if (this.busy()) {
      return;
    }

    this.#validated = true;
    const errors: Record<string, string> = {};
    for (const field of this.data.fields) {
      const error = field.validate(this.data.model, this.data.context.locale);
      if (error !== null) {
        errors[field.field] = error;
      }
    }

    this.errors.set(errors);
    if (Object.keys(errors).length > 0) {
      return;
    }

    this.busy.set(true);
    this.serverError.set(null);
    let error: string | null;
    try {
      error = await this.data.save(this.data.model);
    } catch (e) {
      error = e instanceof Error ? e.message : String(e);
    } finally {
      this.busy.set(false);
    }

    this.serverError.set(error);
    if (error === null) {
      this.ref.close(this.data.model);
    }
  }
}

export interface UiPickerDialogData {
  lookup: UiLookup;
  title: string;
  context: UiRenderContext;
}

/** Lookup dialog of a picker field: a grid of the lookup entity with its search fields; closes with the selected row. */
@Component({
  selector: 'middt-ui-picker-dialog',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [MatDialogModule, MatButtonModule, MatIconModule, forwardRef(() => UiDataGridComponent)],
  template: `
    <h2 mat-dialog-title>{{ data.title }}</h2>
    <mat-dialog-content>
      <middt-ui-data-grid [entity]="data.lookup.entity" [columns]="columns()" [searchFields]="data.lookup.search" [context]="data.context"
                          idPrefix="ui-picker" actionsWidth="80px" [rowActions]="select" />
      <ng-template #select let-row>
        <button mat-icon-button type="button" (click)="ref.close(row)" title="Seç" aria-label="Seç"><mat-icon>check</mat-icon></button>
      </ng-template>
    </mat-dialog-content>
    <mat-dialog-actions>
      <button mat-stroked-button type="button" (click)="ref.close()">İptal</button>
    </mat-dialog-actions>
  `,
})
export class UiPickerDialogComponent {
  protected readonly data = inject<UiPickerDialogData>(MAT_DIALOG_DATA);
  protected readonly ref = inject<MatDialogRef<UiPickerDialogComponent, JsonObject>>(MatDialogRef);

  protected readonly columns = computed(() => {
    const lookup = this.data.lookup;
    if (lookup.columns.length > 0) {
      return lookup.columns;
    }

    const column = new UiColumn();
    column.field = lookup.textField;
    column.title = lookup.textField;
    return [column];
  });
}
