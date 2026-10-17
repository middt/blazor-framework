import { NgTemplateOutlet } from '@angular/common';
import { afterNextRender, ChangeDetectionStrategy, Component, computed, DestroyRef, forwardRef, inject, input, linkedSignal, signal, TemplateRef } from '@angular/core';
import { MatButtonModule } from '@angular/material/button';
import { MatCardModule } from '@angular/material/card';
import { MatIconModule } from '@angular/material/icon';
import { MatPaginatorIntl, MatPaginatorModule, type PageEvent } from '@angular/material/paginator';
import { MatProgressBarModule } from '@angular/material/progress-bar';
import { MatSortModule, type Sort } from '@angular/material/sort';
import { MatTableModule } from '@angular/material/table';
import { errorText, isSuccess, UiDataClient, UiExpressions, type UiColumn, type UiField, type UiRenderContext } from '@middt/ui-core';
import { UI_DATA_CLIENT, NotifyService } from '@middt/angular/core';
import { UiPaginatorIntl } from '@middt/angular/core';
export { UiPaginatorIntl } from '@middt/angular/core';
import { UiFieldEditorComponent } from './field-editor.component';
import { cloneJson, type JsonObject } from './widget-base';

export interface UiRowActionContext {
  $implicit: JsonObject;
}

/**
 * Server paged data grid of an entity (UiDataGrid): search form with field editors, sorting, paging, toolbar and row
 * actions. Project toolbar content with the `toolbar` attribute.
 */
@Component({
  selector: 'middt-ui-data-grid',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [
    NgTemplateOutlet,
    MatTableModule,
    MatPaginatorModule,
    MatSortModule,
    MatProgressBarModule,
    MatButtonModule,
    MatIconModule,
    MatCardModule,
    forwardRef(() => UiFieldEditorComponent),
  ],
  providers: [{ provide: MatPaginatorIntl, useClass: UiPaginatorIntl }],
  host: { style: 'display: contents' },
  template: `
    <div class="middt-ui-grid" [attr.data-entity]="entity()">
      @if (searchFields().length > 0) {
        <mat-card appearance="outlined" class="middt-ui-grid-card">
          <mat-card-content>
            <form class="middt-ui-form" role="search" [attr.aria-label]="(title() ?? entity()) + ' arama'" (submit)="$event.preventDefault(); search()">
              <div class="middt-ui-search">
                @for (field of searchFields(); track field.field) {
                  <middt-ui-field-editor [field]="field" [target]="filter()" [context]="context()" [idPrefix]="idPrefix() + '-search'" />
                }
              </div>
              <div class="middt-ui-toolbar">
                <button mat-flat-button type="submit"><mat-icon>search</mat-icon> Ara</button>
                <button mat-stroked-button type="button" (click)="clear()"><mat-icon>search_off</mat-icon> Temizle</button>
              </div>
            </form>
          </mat-card-content>
        </mat-card>
      }

      @if (title() !== null || hasToolbar()) {
        <div class="middt-ui-toolbar" style="justify-content: space-between">
          @if (title() !== null) {
            <h3 class="middt-ui-section-title">{{ title() }}</h3>
          }
          <div class="middt-ui-toolbar"><ng-content select="[toolbar]" /></div>
        </div>
      }

      <div class="middt-ui-grid-table" [attr.aria-busy]="loading()">
        @if (loading()) {
          <mat-progress-bar mode="indeterminate" aria-label="Yükleniyor" />
        }
        <table mat-table [dataSource]="rows()" [trackBy]="trackRow" matSort (matSortChange)="onSort($event)">
          @for (column of columns(); track $index; let c = $index) {
            <ng-container [matColumnDef]="'c' + c">
              <th mat-header-cell *matHeaderCellDef [mat-sort-header]="'c' + c" [disabled]="!column.sortable" [style.width]="column.width">{{ column.title }}</th>
              <td mat-cell *matCellDef="let row; let r = index">{{ cells()[r]?.[c] }}</td>
            </ng-container>
          }
          @if (rowActions(); as actions) {
            <ng-container matColumnDef="actions">
              <th mat-header-cell *matHeaderCellDef [style.width]="actionsWidth()">İşlemler</th>
              <td mat-cell *matCellDef="let row">
                <div class="middt-ui-toolbar"><ng-container *ngTemplateOutlet="actions; context: { $implicit: row }" /></div>
              </td>
            </ng-container>
          }
          <tr mat-header-row *matHeaderRowDef="displayedColumns()"></tr>
          <tr mat-row *matRowDef="let row; columns: displayedColumns()"></tr>
          <tr class="mat-mdc-row" *matNoDataRow>
            <td class="mat-mdc-cell middt-ui-grid-empty" [attr.colspan]="displayedColumns().length">
              {{ searched() ? 'Kayıt bulunamadı.' : 'Aramak için Ara düğmesini kullanın.' }}
            </td>
          </tr>
        </table>
      </div>
      <mat-paginator [length]="count()" [pageIndex]="page() - 1" [pageSize]="currentPageSize()" [pageSizeOptions]="pageSizeOptions"
                     showFirstLastButtons (page)="onPage($event)" />
    </div>
  `,
  styles: `
    .middt-ui-grid { display: flex; flex-direction: column; gap: .5rem; }
    .middt-ui-grid-table { overflow-x: auto; min-height: 240px; }
    .middt-ui-grid-table table { width: 100%; }
    .middt-ui-grid-empty { padding: 1.5rem 1rem; color: var(--mat-sys-on-surface-variant, #6c757d); text-align: center; }
  `,
})
export class UiDataGridComponent {
  readonly #dataClient = inject(UI_DATA_CLIENT);
  readonly #notify = inject(NotifyService);
  readonly #abort = new AbortController();
  #request = 0;

  readonly entity = input.required<string>();
  readonly columns = input.required<UiColumn[]>();
  readonly searchFields = input<UiField[]>([]);
  /** Fixed filter values merged into every request (e.g. the parent id). */
  readonly fixedFilter = input<(() => JsonObject | null) | null>(null);
  readonly context = input.required<UiRenderContext>();
  readonly title = input<string | null>(null);
  /** Shows the toolbar row (content projected with the `toolbar` attribute). */
  readonly hasToolbar = input(false);
  readonly rowActions = input<TemplateRef<UiRowActionContext> | null>(null);
  readonly actionsWidth = input('140px');
  readonly pageSize = input(10);
  /** Loads the first page when shown (otherwise on the first search). */
  readonly autoLoad = input(true);
  readonly idPrefix = input('ui-grid');

  protected readonly pageSizeOptions = [10, 20, 50, 100];
  protected readonly filter = signal<JsonObject>({});
  protected readonly rows = signal<JsonObject[]>([]);
  protected readonly count = signal(0);
  protected readonly loading = signal(false);
  protected readonly searched = signal(false);
  protected readonly page = signal(1);
  protected readonly currentPageSize = linkedSignal(() => this.pageSize());
  #sortProperty: string | null = null;
  #ascending = false;

  protected readonly displayedColumns = computed(() => [...this.columns().map((_, i) => `c${i}`), ...(this.rowActions() ? ['actions'] : [])]);

  constructor() {
    inject(DestroyRef).onDestroy(() => this.#abort.abort());
    afterNextRender(() => {
      if (this.autoLoad()) {
        this.searched.set(true);
        void this.#load();
      }
    });
  }

  /** Reloads the current page (e.g. after a change). */
  reload(): Promise<void> {
    this.searched.set(true);
    return this.#load();
  }

  protected search(): void {
    this.searched.set(true);
    this.page.set(1);
    void this.#load();
  }

  protected clear(): void {
    this.filter.set({});
    this.search();
  }

  protected onPage(event: PageEvent): void {
    this.currentPageSize.set(event.pageSize);
    this.page.set(event.pageIndex + 1);
    if (this.searched()) {
      void this.#load();
    }
  }

  protected onSort(sort: Sort): void {
    const column = this.columns()[Number(sort.active.slice(1))];
    this.#sortProperty = sort.direction === '' || column === undefined ? null : column.sortProperty;
    this.#ascending = sort.direction === 'asc';
    if (this.searched()) {
      void this.#load();
    }
  }

  /** Cell texts (rows × columns), rendered once per page/columns/context instead of on every change detection pass. */
  protected readonly cells = computed(() => {
    const context = this.context();
    const columns = this.columns();
    return this.rows().map(row => {
      const rowContext = columns.some(c => c.template !== null) ? context.with(UiExpressions.row, row) : context;
      return columns.map(column =>
        column.template !== null ? rowContext.text(column.template, column.format) : context.format(row[column.field], column.format),
      );
    });
  });

  protected readonly trackRow = (index: number): number => index;

  async #load(): Promise<void> {
    const request = ++this.#request;
    this.loading.set(true);
    try {
      const requestFilter = cloneJson(this.filter());
      const fixedFilter = this.fixedFilter()?.() ?? null;
      for (const [name, value] of Object.entries(fixedFilter ?? {})) {
        requestFilter[name] = cloneJson(value);
      }

      const response = await this.#dataClient.getItems<JsonObject>(
        this.entity(),
        UiDataClient.searchRequest(requestFilter, this.page(), this.currentPageSize(), this.#sortProperty, this.#ascending),
        this.#abort.signal,
      );
      if (request !== this.#request) {
        return;
      }

      if (isSuccess(response)) {
        this.rows.set(response.data ?? []);
        this.count.set(response.count);
      } else {
        this.rows.set([]);
        this.count.set(0);
        this.#notify.error('Hata', errorText(response));
      }
    } catch (error) {
      if (!this.#abort.signal.aborted) {
        this.#notify.error('Hata', error instanceof Error ? error.message : String(error));
      }
    } finally {
      if (request === this.#request) {
        this.loading.set(false);
      }
    }
  }
}
