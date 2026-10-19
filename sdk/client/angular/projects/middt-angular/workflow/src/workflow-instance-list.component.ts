import { ChangeDetectionStrategy, Component, computed, DestroyRef, effect, inject, input, linkedSignal, output, signal, untracked, viewChild, ViewEncapsulation } from '@angular/core';
import { MatButtonModule } from '@angular/material/button';
import { MatCardModule } from '@angular/material/card';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatIconModule } from '@angular/material/icon';
import { MatInputModule } from '@angular/material/input';
import { MatPaginatorModule, PageEvent } from '@angular/material/paginator';
import { MatSelectModule } from '@angular/material/select';
import { MatTableModule } from '@angular/material/table';
import {
  formatWorkflowDataCell,
  formatWorkflowDate,
  isSuccess,
  errorText,
  schemaHasInput,
  schemaValuesFromQuery,
  workflowDataColumns,
  workflowSearchColumns,
  WORKFLOW_STATUS_OPTIONS,
  workflowStatusBadge,
  workflowStatusText,
} from '@middt/ui-core';
import type { WorkflowData, WorkflowDataColumn, WorkflowDefinitionModel, WorkflowInstanceModel, WorkflowInstanceStatusName } from '@middt/ui-core';
import { NotifyService } from '@middt/angular/core';
import { MiddtJsonSchemaFormComponent } from './json-schema-form.component';
import { WORKFLOW_API_CLIENT } from './workflow-client';
import { MiddtWorkflowDialogComponent } from './workflow-dialog.component';
import { WF_SHARED_STYLES } from './workflow-styles';

/**
 * Server-paged list of the instances of a workflow (counterpart of WorkflowInstanceList.razor) with a status filter and a
 * "Yeni başlat" dialog built from the start input schema. Workflows whose start takes no input (e.g. wizards that collect the
 * data in their first step) start immediately. `openStart` (e.g. a `?start=1` link) or `showStartDialog()` open the dialog
 * programmatically; with `startKey` / `startValues` (e.g. `?key=…&{property}=…`) the dialog opened by `openStart` is prefilled.
 * Starting with the business key of an existing instance returns (and selects) that instance. State titles are resolved from
 * each instance's definition version.
 */
/** Payload of `startOpened`: the prefilled business key and the names of the applied `startValues`. */
export interface WorkflowStartOpened {
  key: string | null;
  names: string[];
}

@Component({
  selector: 'middt-workflow-instance-list',
  imports: [
    MatCardModule, MatButtonModule, MatIconModule, MatSelectModule, MatFormFieldModule, MatInputModule, MatTableModule, MatPaginatorModule,
    MiddtWorkflowDialogComponent, MiddtJsonSchemaFormComponent,
  ],
  encapsulation: ViewEncapsulation.None,
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <mat-card appearance="outlined" class="middt-wf-card middt-wf-list">
      <mat-card-content>
        <div class="middt-wf-toolbar">
          <h2 class="middt-wf-heading">{{ title() ?? definition()?.title ?? workflow() }}</h2>
          <div class="middt-wf-toolbar-actions">
            @for (col of searchColumns(); track col.key) {
              @if (col.options && col.options.length > 0) {
                <mat-form-field class="middt-wf-filter" subscriptSizing="dynamic">
                  <mat-select [id]="idPrefix() + '-filter-' + col.key" [value]="dataFilterValue(col.key)" (selectionChange)="onDataFilterChanged(col.key, $event.value || null)"
                              [placeholder]="'Tüm ' + col.title" [aria-label]="col.title + ' filtrele'" [attr.title]="col.title + ' filtrele'">
                    <mat-option value="">Tüm {{ col.title }}</mat-option>
                    @for (option of col.options; track option) {
                      <mat-option [value]="option">{{ option }}</mat-option>
                    }
                  </mat-select>
                </mat-form-field>
              } @else if (col.type === 'number' || col.type === 'integer') {
                <mat-form-field class="middt-wf-filter middt-wf-filter-number" subscriptSizing="dynamic">
                  <input matInput type="number" [step]="col.type === 'integer' ? 1 : 'any'" [id]="idPrefix() + '-filter-' + col.key"
                         [value]="dataFilterValue(col.key)"
                         (change)="onDataFilterChanged(col.key, $any($event.target).value || null)"
                         (keydown.enter)="$event.preventDefault(); onDataFilterChanged(col.key, $any($event.target).value || null)"
                         [placeholder]="col.title" [attr.aria-label]="col.title" [attr.title]="col.title" />
                </mat-form-field>
              } @else if (col.type === 'date') {
                <mat-form-field class="middt-wf-filter middt-wf-filter-date" subscriptSizing="dynamic">
                  <input matInput type="date" [id]="idPrefix() + '-filter-' + col.key" [value]="dataFilterValue(col.key)"
                         (change)="onDataFilterChanged(col.key, $any($event.target).value || null)"
                         [placeholder]="col.title" [attr.aria-label]="col.title" [attr.title]="col.title" />
                </mat-form-field>
              } @else {
                <mat-form-field class="middt-wf-filter middt-wf-filter-search" subscriptSizing="dynamic">
                  <input matInput type="search" [id]="idPrefix() + '-filter-' + col.key" [value]="dataFilterValue(col.key)"
                         (change)="onDataFilterChanged(col.key, $any($event.target).value || null)"
                         (keydown.enter)="$event.preventDefault(); onDataFilterChanged(col.key, $any($event.target).value || null)"
                         [placeholder]="col.title + ' ara...'" [attr.aria-label]="col.title + ' ara'" [attr.title]="col.title + ' ara'" />
                </mat-form-field>
              }
            }
            @if (showStatusFilter()) {
              <mat-form-field class="middt-wf-filter" subscriptSizing="dynamic">
                <mat-select [value]="statusFilter() ?? ''" (selectionChange)="onStatusFilterChanged($event.value || null)" placeholder="Tüm durumlar"
                            aria-label="Duruma göre filtrele" title="Duruma göre filtrele">
                  <mat-option value="">Tüm durumlar</mat-option>
                  @for (option of statusOptions; track option.value) {
                    <mat-option [value]="option.value">{{ option.text }}</mat-option>
                  }
                </mat-select>
              </mat-form-field>
            }
            <button mat-icon-button type="button" title="Listeyi yenile" aria-label="Listeyi yenile" (click)="refresh()"><mat-icon>refresh</mat-icon></button>
            @if (allowStart()) {
              <button mat-flat-button type="button" class="middt-wf-start" title="Yeni akış başlat" (click)="onStartClick()"
                      [disabled]="definition() === null || (starting() && !startOpen())">
                <mat-icon>{{ starting() && !startOpen() ? 'hourglass_empty' : 'add' }}</mat-icon>{{ starting() && !startOpen() ? 'Başlatılıyor...' : 'Yeni başlat' }}
              </button>
            }
          </div>
        </div>

        <div class="middt-wf-grid" [attr.aria-busy]="loading()">
          <table mat-table [dataSource]="items()" class="middt-wf-table">
            <ng-container matColumnDef="key">
              <th mat-header-cell *matHeaderCellDef>Anahtar</th>
              <td mat-cell *matCellDef="let item">{{ item.key ?? item.id.slice(0, 8) }}</td>
            </ng-container>
            @for (col of dataColumns(); track col.key) {
              <ng-container [matColumnDef]="'data:' + col.key">
                <th mat-header-cell *matHeaderCellDef>{{ col.title }}</th>
                <td mat-cell *matCellDef="let item">{{ dataCell(item.data, col) }}</td>
              </ng-container>
            }
            <ng-container matColumnDef="state">
              <th mat-header-cell *matHeaderCellDef>Adım</th>
              <td mat-cell *matCellDef="let item">{{ stateTitle(item) }}</td>
            </ng-container>
            <ng-container matColumnDef="status">
              <th mat-header-cell *matHeaderCellDef>Durum</th>
              <td mat-cell *matCellDef="let item"><span class="middt-wf-badge middt-wf-badge-{{ badge(item.status) }}">{{ statusText(item.status) }}</span></td>
            </ng-container>
            <ng-container matColumnDef="createdAt">
              <th mat-header-cell *matHeaderCellDef>Oluşturma</th>
              <td mat-cell *matCellDef="let item">{{ date(item.createdAt) }}</td>
            </ng-container>
            <ng-container matColumnDef="updatedAt">
              <th mat-header-cell *matHeaderCellDef>Güncelleme</th>
              <td mat-cell *matCellDef="let item">{{ date(item.updatedAt) }}</td>
            </ng-container>
            <tr mat-header-row *matHeaderRowDef="columns()"></tr>
            <tr mat-row *matRowDef="let item; columns: columns()" class="middt-wf-row" [class.middt-wf-row-selected]="item.id === selectedRowId()"
                [attr.aria-selected]="item.id === selectedRowId()" tabindex="0" [attr.data-id]="item.id"
                (click)="select(item)" (keydown.enter)="select(item)" (keydown.space)="$event.preventDefault(); select(item)"></tr>
            <tr class="mat-mdc-row" *matNoDataRow>
              <td class="mat-mdc-cell middt-wf-empty" [attr.colspan]="columns().length">{{ loading() ? 'Yükleniyor...' : 'Kayıt bulunamadı.' }}</td>
            </tr>
          </table>
        </div>
        <mat-paginator [length]="count()" [pageIndex]="page() - 1" [pageSize]="currentPageSize()" [pageSizeOptions]="[10, 20, 50]"
                       [showFirstLastButtons]="true" (page)="onPage($event)" aria-label="Sayfalar" />
      </mat-card-content>
    </mat-card>

    <middt-wf-dialog [dialogId]="idPrefix() + '-start-dialog'" [visible]="startOpen()" [title]="'Yeni başlat: ' + (definition()?.title ?? workflow())"
                     [busy]="starting()" (closed)="closeStartDialog()">
      <form [id]="idPrefix() + '-start-form'" novalidate (submit)="$event.preventDefault(); start()">
        <div class="middt-jsf-field">
          <label [for]="idPrefix() + '-start-key'" class="middt-jsf-label">İş anahtarı (opsiyonel)</label>
          <mat-form-field class="middt-jsf-input" subscriptSizing="dynamic">
            <input matInput [id]="idPrefix() + '-start-key'" [name]="idPrefix() + '-start-key'" maxlength="200" [value]="keyText()" [disabled]="starting()"
                   placeholder="Aynı anahtarla tekrar başlatılırsa mevcut kayıt döner" (input)="keyText.set($any($event.target).value)" />
          </mat-form-field>
        </div>
        @if (startOpen()) {
          <middt-json-schema-form #startForm [schema]="definition()?.startInputSchema" [value]="startData()" [idPrefix]="idPrefix() + '-start'" [disabled]="starting()" />
        }
        @if (startErrors().length > 0) {
          <ul class="middt-wf-errors" role="alert">
            @for (error of startErrors(); track $index) {
              <li>{{ error }}</li>
            }
          </ul>
        }
      </form>
      <ng-container footer>
        <button mat-button type="button" (click)="closeStartDialog()" [disabled]="starting()">Vazgeç</button>
        <button mat-flat-button type="submit" [attr.form]="idPrefix() + '-start-form'" class="middt-wf-start-submit" [disabled]="starting()">
          <mat-icon>{{ starting() ? 'hourglass_empty' : 'play_arrow' }}</mat-icon>{{ starting() ? 'Başlatılıyor...' : 'Başlat' }}
        </button>
      </ng-container>
    </middt-wf-dialog>
  `,
  styles: [
    WF_SHARED_STYLES,
    `
    .middt-wf-filter { width: 170px; }
    .middt-wf-filter-search { width: 200px; }
    .middt-wf-filter-number { width: 155px; }
    .middt-wf-filter-date { width: 150px; }
    .middt-wf-grid { overflow-x: auto; }
    .middt-wf-table { width: 100%; }
    .middt-wf-row { cursor: pointer; }
    .middt-wf-row:hover { background: var(--mat-sys-surface-container-high, #f2f2f2); }
    .middt-wf-row:focus-visible { outline: 2px solid var(--mat-sys-primary, #1976d2); outline-offset: -2px; }
    .middt-wf-row-selected, .middt-wf-row-selected:hover { background: var(--mat-sys-secondary-container, #e3ecfa); }
    .middt-wf-empty { padding: 1rem; text-align: center; color: var(--mat-sys-on-surface-variant, #6c757d); }
    `,
  ],
})
export class MiddtWorkflowInstanceListComponent {
  /** Workflow key (e.g. `order-approval`). */
  readonly workflow = input.required<string>();
  /** Heading; defaults to the workflow title. */
  readonly title = input<string | null>(null);
  /** Only list instances in this state. */
  readonly state = input<string | null>(null);
  /** Initial status filter. */
  readonly status = input<WorkflowInstanceStatusName | null>(null);
  readonly showStatusFilter = input(true);
  /** Shows the "Yeni başlat" button. */
  readonly allowStart = input(true);
  readonly pageSize = input(10);
  /** Prefix of element ids (unique per page when several lists are shown). */
  readonly idPrefix = input('wf');
  /** Id of the instance highlighted in the list. */
  readonly selectedId = input<string | null>(null);
  /**
   * Opens the start dialog once the definition is loaded (e.g. a "start new" link like `?start=1`). Only honoured when
   * `allowStart` is set; it opens again only after the value went back to `false`. The dialog is always shown (also for
   * workflows without start input) so nothing starts without a click.
   */
  readonly openStart = input(false);
  /** Business key prefilled when `openStart` opens the dialog (the user can change it). */
  readonly startKey = input<string | null>(null);
  /**
   * Start form values prefilled when `openStart` opens the dialog, e.g. URL query parameters: only properties of the start
   * input schema are taken, texts are converted by the property type (`schemaValuesFromQuery`), others are ignored. Applied
   * once per opening; nothing starts without the user's "Başlat".
   */
  readonly startValues = input<Readonly<Record<string, unknown>> | null>(null);
  /** Raised when the user selects an instance (or after starting one). */
  readonly selected = output<WorkflowInstanceModel>();
  /**
   * Raised after `openStart` opened the dialog (e.g. to remove the query from the URL) with the names of the `startValues`
   * that were applied.
   */
  readonly startOpened = output<WorkflowStartOpened>();
  /** Raised when the current definition of the workflow is loaded (e.g. to show its title as the browser title). */
  readonly definitionLoaded = output<WorkflowDefinitionModel>();

  protected readonly statusOptions = WORKFLOW_STATUS_OPTIONS;

  protected readonly definition = signal<WorkflowDefinitionModel | null>(null);
  protected readonly dataColumns = computed<WorkflowDataColumn[]>(() => workflowDataColumns(this.definition()));
  protected readonly searchColumns = computed<WorkflowDataColumn[]>(() => workflowSearchColumns(this.definition()));
  protected readonly dataFilters = signal<Readonly<Record<string, string>>>({});
  protected readonly columns = computed<string[]>(() => {
    const cols = this.dataColumns();
    return ['key', ...cols.map(c => 'data:' + c.key), 'state', 'status', ...(cols.length === 0 ? ['createdAt'] : []), 'updatedAt'];
  });
  readonly #definitions = signal<ReadonlyMap<number, WorkflowDefinitionModel>>(new Map());
  protected readonly items = signal<WorkflowInstanceModel[]>([]);
  protected readonly count = signal(0);
  protected readonly loading = signal(false);
  protected readonly page = signal(1);
  readonly #pageSize = signal<number | null>(null);
  protected readonly currentPageSize = computed(() => this.#pageSize() ?? this.pageSize());
  protected readonly statusFilter = linkedSignal(() => this.status());
  protected readonly selectedRowId = linkedSignal(() => this.selectedId());

  protected readonly startOpen = signal(false);
  protected readonly starting = signal(false);
  protected readonly keyText = signal('');
  /** Initial value of the start form (prefill of `openStart`; null when opened by the user). */
  protected readonly startData = signal<WorkflowData | null>(null);
  protected readonly startErrors = signal<string[]>([]);
  private readonly startForm = viewChild<MiddtJsonSchemaFormComponent>('startForm');

  readonly #client = inject(WORKFLOW_API_CLIENT);
  readonly #notify = inject(NotifyService);
  #abort = new AbortController();
  #pageRequest = 0;
  #openStartRequested = false;

  constructor() {
    inject(DestroyRef).onDestroy(() => this.#abort.abort());
    effect(() => {
      const workflow = this.workflow();
      this.state();
      untracked(() => void this.#loadAll(workflow));
    });
    effect(() => {
      if (!this.openStart()) {
        this.#openStartRequested = false;
        return;
      }

      if (this.#openStartRequested || this.definition() === null) {
        return;
      }

      this.#openStartRequested = true;
      untracked(() => {
        if (this.allowStart() && !this.starting()) {
          const prefill = schemaValuesFromQuery(this.definition()?.startInputSchema, this.startValues());
          const key = this.startKey()?.trim() ?? '';
          this.#openStartDialog(key, prefill.names.length > 0 ? prefill.data : null);
          this.startOpened.emit({ key: key === '' ? null : key, names: prefill.names });
        }
      });
    });
  }

  /** Opens the start dialog (same as the "Yeni başlat" button, but always with the dialog). */
  showStartDialog(): void {
    if (this.allowStart() && this.definition() !== null && !this.starting()) {
      this.#openStartDialog();
    }
  }

  #openStartDialog(key = '', data: WorkflowData | null = null): void {
    this.keyText.set(key);
    this.startData.set(data);
    this.startErrors.set([]);
    this.startForm()?.reset(data);
    this.startOpen.set(true);
  }

  /** Reloads the current page. */
  refresh(): Promise<void> {
    return this.#loadPage();
  }

  protected stateTitle(item: WorkflowInstanceModel): string {
    const definition = this.#definitions().get(item.workflowVersion);
    return item.stateTitle ?? definition?.states.find(s => s.key === item.state)?.title ?? item.state;
  }

  protected statusText = workflowStatusText;
  protected badge = workflowStatusBadge;
  protected date = formatWorkflowDate;
  protected dataCell = formatWorkflowDataCell;

  async #loadAll(workflow: string): Promise<void> {
    this.#abort.abort();
    this.#abort = new AbortController();
    this.definition.set(null);
    this.#definitions.set(new Map());
    this.dataFilters.set({});
    this.page.set(1);
    const response = await this.#client.getDefinition(workflow, null, this.#abort.signal).catch(() => null);
    if (response === null) {
      return;
    }

    if (isSuccess(response) && response.data) {
      this.definition.set(response.data);
      this.#addDefinition(response.data);
      this.definitionLoaded.emit(response.data);
    } else {
      this.#notify.error('Akış tanımı alınamadı', errorText(response));
    }

    await this.#loadPage();
  }

  async #loadPage(): Promise<void> {
    const request = ++this.#pageRequest;
    const signal = this.#abort.signal;
    this.loading.set(true);
    try {
      const filters = this.dataFilters();
      const response = await this.#client.listInstances(
        this.workflow(),
        {
          state: this.state(),
          status: this.statusFilter(),
          data: Object.keys(filters).length > 0 ? filters : null,
          page: this.page(),
          pageSize: this.currentPageSize(),
        },
        signal,
      );
      if (request !== this.#pageRequest) {
        return;
      }

      if (isSuccess(response)) {
        this.items.set(response.data ?? []);
        this.count.set(response.count);
        await this.#loadVersions(this.items());
      } else {
        this.items.set([]);
        this.count.set(0);
        this.#notify.error('Kayıtlar alınamadı', errorText(response));
      }
    } catch {
      // aborted (workflow changed or component destroyed)
    } finally {
      if (request === this.#pageRequest) {
        this.loading.set(false);
      }
    }
  }

  /** Definitions of the versions the listed instances run on (older instances keep their version's state titles). */
  async #loadVersions(items: readonly WorkflowInstanceModel[]): Promise<void> {
    const missing = [...new Set(items.filter(i => !i.stateTitle).map(i => i.workflowVersion))].filter(v => !this.#definitions().has(v));
    const workflow = this.workflow();
    const responses = await Promise.all(missing.map(v => this.#client.getDefinition(workflow, v, this.#abort.signal)));
    for (const response of responses) {
      if (isSuccess(response) && response.data) {
        this.#addDefinition(response.data);
      }
    }
  }

  #addDefinition(definition: WorkflowDefinitionModel): void {
    this.#definitions.update(map => new Map(map).set(definition.version, definition));
  }

  protected onPage(event: PageEvent): void {
    this.#pageSize.set(event.pageSize);
    this.page.set(event.pageIndex + 1);
    void this.#loadPage();
  }

  protected dataFilterValue(key: string): string {
    return this.dataFilters()[key] ?? '';
  }

  protected onDataFilterChanged(key: string, value: string | null): void {
    const trimmed = value?.trim() ?? '';
    const current = this.dataFilters();
    if ((current[key] ?? '') === trimmed) {
      return;
    }

    const next = { ...current };
    if (trimmed === '') {
      delete next[key];
    } else {
      next[key] = trimmed;
    }

    this.dataFilters.set(next);
    this.page.set(1);
    void this.#loadPage();
  }

  protected onStatusFilterChanged(value: WorkflowInstanceStatusName | null): void {
    this.statusFilter.set(value);
    this.page.set(1);
    void this.#loadPage();
  }

  protected select(item: WorkflowInstanceModel): void {
    this.selectedRowId.set(item.id);
    this.selected.emit(item);
  }

  protected onStartClick(): void {
    if (schemaHasInput(this.definition()?.startInputSchema)) {
      this.#openStartDialog();
      return;
    }

    this.keyText.set('');
    this.startData.set(null);
    this.startErrors.set([]);

    // Nothing to ask: start right away and select the new instance (its first step is shown by the detail).
    void this.start();
  }

  protected closeStartDialog(): void {
    if (!this.starting()) {
      this.startOpen.set(false);
    }
  }

  protected async start(): Promise<void> {
    const open = this.startOpen();
    const form = this.startForm();
    if (this.starting() || (open && form !== undefined && !form.validate())) {
      return;
    }

    this.starting.set(true);
    this.startErrors.set([]);
    try {
      const key = this.keyText().trim();
      const response = await this.#client.start(this.workflow(), { key: key === '' ? null : key, data: open ? (form?.getValue() ?? {}) : {} }, this.#abort.signal);
      const instance = response.data;
      if (!isSuccess(response) || !instance) {
        const errors = response.messageList.length > 0 ? response.messageList : ['Akış başlatılamadı.'];
        this.startErrors.set(errors);
        if (!open) {
          this.#notify.error('Akış başlatılamadı', errors.join(' '));
        }

        return;
      }

      this.startOpen.set(false);
      // A start with the business key of an existing instance returns that instance with a message (nothing started).
      if (response.messageList.length > 0) {
        this.#notify.info('Mevcut akış açıldı', response.messageList.join(' '));
      } else {
        this.#notify.success('Akış başlatıldı', `${instance.stateTitle ?? instance.state} (${workflowStatusText(instance.status)})`);
      }
      this.selectedRowId.set(instance.id);
      this.page.set(1);
      await this.#loadPage();
      this.selected.emit(instance);
    } catch {
      // aborted
    } finally {
      this.starting.set(false);
    }
  }
}
