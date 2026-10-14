import { ChangeDetectionStrategy, Component, computed, forwardRef, inject, viewChild } from '@angular/core';
import { MatButtonModule } from '@angular/material/button';
import { MatDialog } from '@angular/material/dialog';
import { MatIconModule } from '@angular/material/icon';
import { MatTooltipModule } from '@angular/material/tooltip';
import { Router } from '@angular/router';
import { errorText, isSuccess, PortableExpressionError, UiColumn, UiExpressions, UiField, UiFieldJson, UiUrl } from '@middt/ui-core';
import { UI_DATA_CLIENT } from '../core/config';
import { NotifyService } from '../core/notify.service';
import { UiDataGridComponent } from './data-grid.component';
import { confirmDialog, UiFormDialogComponent, type UiFormDialogData } from './dialogs';
import { cloneJson, isJsonObject, type JsonObject, UiProps, UiWidgetBase } from './widget-base';

interface RowAction {
  text: string | null;
  icon: string | null;
  style: string | null;
  navigate: string | null;
}

/** `grid` widget: server paged grid with search, fixed filter, create/edit/delete and navigation row actions (UiGridWidget). */
@Component({
  selector: 'middt-ui-grid-widget',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [MatButtonModule, MatIconModule, MatTooltipModule, forwardRef(() => UiDataGridComponent)],
  host: { style: 'display: contents' },
  template: `
    <middt-ui-data-grid [entity]="entity()" [columns]="columns()" [searchFields]="search()" [fixedFilter]="fixedFilter" [context]="context()"
                        [title]="title()" [pageSize]="pageSize()" [autoLoad]="autoLoad()" [idPrefix]="elementId()"
                        [hasToolbar]="canCreate()" [rowActions]="hasRowActions() ? rowButtons : null" [actionsWidth]="actionsWidth()">
      @if (canCreate()) {
        <button toolbar mat-flat-button type="button" (click)="create()"><mat-icon>add</mat-icon> Yeni</button>
      }
    </middt-ui-data-grid>

    <ng-template #rowButtons let-row>
      @for (action of actions(); track $index) {
        @if (action.icon) {
          <button mat-icon-button type="button" [class]="'middt-ui-action-' + (action.style ?? 'primary')" [attr.title]="action.text"
                  [attr.aria-label]="action.text ?? 'Aç'" (click)="navigate(action, row)"><mat-icon>{{ action.icon }}</mat-icon></button>
        } @else {
          <button mat-stroked-button type="button" [class]="'middt-ui-action-' + (action.style ?? 'primary')" [attr.title]="action.text"
                  [attr.aria-label]="action.text ?? 'Aç'" (click)="navigate(action, row)"><mat-icon>open_in_new</mat-icon> {{ action.text }}</button>
        }
      }
      @if (canEdit()) {
        <button mat-icon-button type="button" title="Düzenle" aria-label="Düzenle" (click)="edit(row)"><mat-icon>edit</mat-icon></button>
      }
      @if (canDelete()) {
        <button mat-icon-button type="button" class="middt-ui-action-danger" title="Sil" aria-label="Sil" (click)="remove(row)"><mat-icon>delete</mat-icon></button>
      }
    </ng-template>
  `,
  styles: `
    .middt-ui-action-danger { color: var(--mat-sys-error, #dc3545); }
  `,
})
export class UiGridWidget extends UiWidgetBase {
  readonly #dataClient = inject(UI_DATA_CLIENT);
  readonly #dialog = inject(MatDialog);
  readonly #router = inject(Router);
  readonly #notify = inject(NotifyService);
  private readonly grid = viewChild.required(UiDataGridComponent);

  protected readonly entity = computed(() => UiProps.string(this.node(), 'entity') ?? '');
  protected readonly columns = computed(() => UiColumn.parseAll(this.node()['columns']));
  protected readonly search = computed(() => UiField.parseAll(this.node()['search']));
  protected readonly form = computed(() => UiField.parseAll(this.node()['form']));
  protected readonly actions = computed<RowAction[]>(() =>
    (UiProps.array(this.node(), 'rowActions') ?? []).filter(isJsonObject).map(a => ({
      text: UiFieldJson.getString(a, 'text'),
      icon: UiFieldJson.getString(a, 'icon'),
      style: UiFieldJson.getString(a, 'style'),
      navigate: UiFieldJson.getString(a, 'navigate'),
    })),
  );
  protected readonly canCreate = computed(() => UiProps.bool(this.node(), 'create') && this.form().length > 0);
  protected readonly canEdit = computed(() => UiProps.bool(this.node(), 'edit') && this.form().length > 0);
  protected readonly canDelete = computed(() => UiProps.bool(this.node(), 'delete'));
  protected readonly hasRowActions = computed(() => this.canEdit() || this.canDelete() || this.actions().length > 0);
  protected readonly actionsWidth = computed(
    () => `${Math.max(70, ((this.canEdit() ? 1 : 0) + (this.canDelete() ? 1 : 0) + this.actions().length) * 48 + 24)}px`,
  );
  protected readonly title = computed(() => (UiProps.string(this.node(), 'title') === null ? null : this.text('title')));
  protected readonly pageSize = computed(() => UiProps.int(this.node(), 'pageSize', 10));
  protected readonly autoLoad = computed(() => UiProps.bool(this.node(), 'autoLoad', true));

  /** Fixed filter rendered with the context (`{ "categoryId": "{{ query.id }}" }`). */
  protected readonly fixedFilter = (): JsonObject | null => {
    const filter = UiProps.object(this.node(), 'filter');
    if (filter === null) {
      return null;
    }

    try {
      const rendered = this.context().render(filter);
      return isJsonObject(rendered) ? rendered : null;
    } catch (error) {
      if (error instanceof PortableExpressionError) {
        this.#notify.error('Filtre hatası', error.message);
        return null;
      }

      throw error;
    }
  };

  protected navigate(action: RowAction, row: JsonObject): void {
    const url = this.context().with(UiExpressions.row, row).text(action.navigate);
    if (UiUrl.isLocal(url)) {
      void this.#router.navigateByUrl(url);
    } else {
      this.#notify.error('Hata', 'Geçersiz bağlantı.');
    }
  }

  protected create(): void {
    void this.#openForm({}, true);
  }

  protected edit(row: JsonObject): void {
    void this.#openForm(cloneJson(row), false);
  }

  protected async remove(row: JsonObject): Promise<void> {
    if (!(await confirmDialog(this.#dialog, { title: 'Sil', message: 'Kayıt silinsin mi?', ok: 'Sil', cancel: 'Vazgeç' }))) {
      return;
    }

    const response = await this.#dataClient.delete(this.entity(), row);
    if (isSuccess(response)) {
      this.#notify.success('Silindi');
      await this.grid().reload();
    } else {
      this.#notify.error('Silinemedi', errorText(response));
    }
  }

  async #openForm(model: JsonObject, insert: boolean): Promise<void> {
    const title = UiProps.string(this.node(), 'formTitle') ?? UiProps.string(this.node(), 'title') ?? this.entity();
    const entity = this.entity();
    const data: UiFormDialogData = {
      title: insert ? `${title} - Yeni` : `${title} - Düzenle`,
      fields: this.form(),
      model,
      context: this.context(),
      save: async m => {
        const response = insert ? await this.#dataClient.insert(entity, m) : await this.#dataClient.update(entity, m);
        return isSuccess(response) ? null : errorText(response);
      },
    };
    const ref = this.#dialog.open<UiFormDialogComponent, UiFormDialogData, JsonObject>(UiFormDialogComponent, {
      data,
      width: 'min(640px, 95vw)',
      maxWidth: '95vw',
    });
    ref.afterClosed().subscribe(saved => {
      if (saved) {
        this.#notify.success('Kaydedildi');
        void this.grid().reload();
      }
    });
  }
}
