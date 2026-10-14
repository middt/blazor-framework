import { ChangeDetectionStrategy, Component, inject, OnInit, output, signal } from '@angular/core';
import { MatButtonModule } from '@angular/material/button';
import { MatCardModule } from '@angular/material/card';
import { MatDialog } from '@angular/material/dialog';
import { MatIconModule } from '@angular/material/icon';
import {
  errorText,
  isSuccess,
  UiExpressions,
  type UiLayoutDocument,
  type UiLayoutRevision,
  type UiLayoutSummary,
  type UiRenderContext,
  type UiWidgetSpecModel,
} from '@middt/ui-core';
import { MIDDT_CONFIG, UI_API_CLIENT } from '../core/config';
import { NotifyService } from '../core/notify.service';
import { confirmDialog } from './dialogs';
import { UiLayoutCache } from './layout-cache.service';
import { parseLayout, serializeLayout } from './layout-json';
import { UiLayoutViewComponent } from './node.component';
import { UiRenderContextFactory } from './render-context';

const TEMPLATE = `{
  "key": "new-page",
  "title": "Yeni sayfa",
  "kind": "page",
  "menu": { "title": "Yeni sayfa", "icon": "dashboard", "order": 100 },
  "body": [
    { "widget": "heading", "text": "Merhaba {{ user.Name }}" },
    { "widget": "text", "text": "Bu sayfa çalışma zamanında eklendi." }
  ]
}`;

/**
 * Runtime layout editor (UiLayoutEditor): list, edit JSON, validate (server), preview, save with optimistic
 * concurrency, revisions. Protect the route with the API's admin role (MiddtUiOptions.AdminRole).
 */
@Component({
  selector: 'middt-layout-editor',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [MatButtonModule, MatCardModule, MatIconModule, UiLayoutViewComponent],
  template: `
    <div class="middt-ui-admin">
      <mat-card appearance="outlined" class="middt-ui-admin-list">
        <mat-card-content>
          <div class="middt-ui-toolbar" style="justify-content: space-between">
            <h2 class="middt-ui-section-title">Yerleşimler</h2>
            <button mat-stroked-button type="button" (click)="newLayout()"><mat-icon>add</mat-icon> Yeni</button>
          </div>
          <ul class="middt-ui-admin-items" aria-label="Yerleşimler">
            @for (item of layouts(); track item.key) {
              <li>
                <button type="button" class="middt-ui-admin-item" [class.middt-ui-admin-item-active]="item.key === selectedKey()"
                        [attr.aria-current]="item.key === selectedKey() ? 'true' : null" (click)="open(item.key)">
                  <span>{{ item.title ?? item.key }}</span>
                  <small>{{ item.key }} · {{ item.kind }} · v{{ item.version }}</small>
                </button>
              </li>
            }
          </ul>
        </mat-card-content>
      </mat-card>

      <div class="middt-ui-admin-main">
        <mat-card appearance="outlined">
          <mat-card-content>
            <div class="middt-ui-toolbar" style="justify-content: space-between">
              <h2 class="middt-ui-section-title">{{ selectedKey() ?? 'Yeni yerleşim' }} {{ version() > 0 ? '(v' + version() + ')' : '' }}</h2>
              <div class="middt-ui-toolbar">
                <button mat-button type="button" (click)="format()"><mat-icon>data_object</mat-icon> Biçimlendir</button>
                <button mat-stroked-button type="button" (click)="validate()"><mat-icon>rule</mat-icon> Doğrula</button>
                <button mat-stroked-button type="button" (click)="previewDraft()"><mat-icon>visibility</mat-icon> Önizle</button>
                <button mat-flat-button type="button" (click)="save()" [disabled]="busy()"><mat-icon>save</mat-icon> Kaydet</button>
                @if (selectedKey() !== null) {
                  <button mat-flat-button type="button" class="middt-ui-danger" (click)="remove()" [disabled]="busy()"><mat-icon>delete</mat-icon> Sil</button>
                }
              </div>
            </div>
            <label for="ui-layout-json" class="middt-wf-sr-only">Yerleşim JSON</label>
            <textarea id="ui-layout-json" class="middt-ui-editor" spellcheck="false" [value]="json()" (input)="json.set($any($event.target).value)"></textarea>
            @if (errors().length > 0) {
              <div class="middt-ui-alert middt-ui-alert-danger" role="alert">
                <strong>{{ errors().length }} hata</strong>
                <ul class="middt-ui-list">
                  @for (e of errors(); track $index) {
                    <li>{{ e }}</li>
                  }
                </ul>
              </div>
            } @else if (validMessage()) {
              <div class="middt-ui-alert middt-ui-alert-success" role="status">{{ validMessage() }}</div>
            }
          </mat-card-content>
        </mat-card>

        @if (preview(); as p) {
          <mat-card appearance="outlined">
            <mat-card-content>
              <h2 class="middt-ui-section-title middt-ui-admin-preview-title">Önizleme</h2>
              @for (k of [previewKey()]; track k) {
                <middt-ui-layout-view [layout]="p.layout" [context]="p.context" />
              }
            </mat-card-content>
          </mat-card>
        }

        @if (revisions().length > 0) {
          <mat-card appearance="outlined">
            <mat-card-content>
              <details>
                <summary>Önceki sürümler ({{ revisions().length }})</summary>
                <ul class="middt-ui-list">
                  @for (revision of revisions(); track revision.version) {
                    <li>
                      v{{ revision.version }} · {{ formatDate(revision.createdAt) }} · {{ revision.createdBy }}
                      <button mat-button type="button" (click)="loadRevision(revision)">Editöre yükle</button>
                    </li>
                  }
                </ul>
              </details>
            </mat-card-content>
          </mat-card>
        }

        <mat-card appearance="outlined">
          <mat-card-content>
            <details>
              <summary>Bileşenler ({{ widgets().length }})</summary>
              @for (widget of widgets(); track widget.name) {
                <div class="middt-ui-admin-widget">
                  <strong>{{ widget.name }}</strong> {{ widget.interactive ? '· etkileşimli' : '' }}
                  {{ widget.children !== 'none' ? '· children: ' + widget.children : '' }}
                  <div class="middt-ui-text middt-ui-text-muted">{{ widget.description }}</div>
                  <code class="middt-ui-text-code">{{ propList(widget) }}</code>
                </div>
              }
            </details>
          </mat-card-content>
        </mat-card>
      </div>
    </div>
  `,
  styles: `
    .middt-wf-sr-only { position: absolute; width: 1px; height: 1px; padding: 0; margin: -1px; overflow: hidden; clip: rect(0, 0, 0, 0); white-space: nowrap; border: 0; }
    .middt-ui-editor { box-sizing: border-box; margin-top: .75rem; padding: .5rem; resize: vertical; color: inherit; background: transparent;
      border: 1px solid var(--mat-sys-outline-variant, #ccc); border-radius: 4px; }
    .middt-ui-admin-main .middt-ui-alert { margin-top: .75rem; }
    .middt-ui-admin-preview-title { margin-bottom: .5rem; }
    .middt-ui-admin-widget { margin-top: .5rem; }
    .middt-ui-danger { --mat-button-filled-container-color: var(--mat-sys-error); --mat-button-filled-label-text-color: var(--mat-sys-on-error); }
  `,
})
export class MiddtLayoutEditorComponent implements OnInit {
  readonly #client = inject(UI_API_CLIENT);
  readonly #layoutCache = inject(UiLayoutCache);
  readonly #notify = inject(NotifyService);
  readonly #dialog = inject(MatDialog);
  readonly #contexts = inject(UiRenderContextFactory);
  readonly #locale = inject(MIDDT_CONFIG).locale;

  /** Raised after a save or delete (e.g. to refresh the menu). */
  readonly changed = output<void>();

  protected readonly layouts = signal<UiLayoutSummary[]>([]);
  protected readonly widgets = signal<UiWidgetSpecModel[]>([]);
  protected readonly revisions = signal<UiLayoutRevision[]>([]);
  protected readonly errors = signal<string[]>([]);
  protected readonly selectedKey = signal<string | null>(null);
  protected readonly version = signal(0);
  protected readonly json = signal(TEMPLATE);
  protected readonly validMessage = signal<string | null>(null);
  protected readonly busy = signal(false);
  protected readonly preview = signal<{ layout: UiLayoutDocument; context: UiRenderContext } | null>(null);
  protected readonly previewKey = signal(0);

  async ngOnInit(): Promise<void> {
    await this.#loadList();
    const response = await this.#client.widgets();
    this.widgets.set(response.data ?? []);
  }

  protected newLayout(): void {
    this.selectedKey.set(null);
    this.version.set(0);
    this.json.set(TEMPLATE);
    this.revisions.set([]);
    this.#resetMessages();
  }

  protected async open(key: string): Promise<void> {
    const response = await this.#client.get(key);
    if (!isSuccess(response) || !response.data) {
      this.#notify.error('Yüklenemedi', errorText(response));
      return;
    }

    this.selectedKey.set(key);
    this.version.set(response.data.version ?? 0);
    this.json.set(serializeLayout(response.data));
    this.#resetMessages();
    const history = await this.#client.revisions(key);
    this.revisions.set(history.data ?? []);
  }

  protected loadRevision(revision: UiLayoutRevision): void {
    try {
      // Keep the current version as the expected version so saving restores the revision as a new version.
      const document = parseLayout(revision.content);
      document.version = this.version();
      this.json.set(serializeLayout(document));
      this.#resetMessages();
    } catch (error) {
      this.errors.set([error instanceof Error ? error.message : String(error)]);
    }
  }

  protected format(): void {
    const document = this.#tryParse();
    if (document !== null) {
      this.json.set(serializeLayout(document));
    }
  }

  protected async validate(): Promise<void> {
    const document = this.#tryParse();
    if (document === null) {
      return;
    }

    const response = await this.#client.validate(document);
    if (!isSuccess(response)) {
      this.errors.set([...response.messageList]);
      return;
    }

    const errors = response.data?.errors ?? [];
    this.errors.set(errors);
    this.validMessage.set(errors.length === 0 ? 'Yerleşim geçerli.' : null);
  }

  /** Renders the draft locally (interactive widgets included); validation errors come from the server catalog. */
  protected async previewDraft(): Promise<void> {
    const document = this.#tryParse();
    if (document === null) {
      return;
    }

    await this.#contexts.ready();
    this.preview.set({ layout: document, context: this.#contexts.create({ [UiExpressions.query]: {} }, 'ui-preview') });
    this.previewKey.update(k => k + 1);
    const response = await this.#client.validate(document);
    this.errors.set(isSuccess(response) ? (response.data?.errors ?? []) : [...response.messageList]);
  }

  protected async save(): Promise<void> {
    const document = this.#tryParse();
    if (document === null) {
      return;
    }

    const selectedKey = this.selectedKey();
    if (selectedKey !== null && document.key !== selectedKey) {
      this.errors.set(["Kayıtlı bir yerleşimin anahtarı değiştirilemez; yeni yerleşim için 'Yeni' kullanın."]);
      return;
    }

    document.version = this.version();
    this.busy.set(true);
    try {
      const response = await this.#client.save(document);
      if (!isSuccess(response) || !response.data) {
        this.errors.set([...response.messageList]);
        return;
      }

      const saved = response.data;
      this.#layoutCache.invalidate(saved.key);
      this.selectedKey.set(saved.key);
      this.version.set(saved.version ?? 0);
      this.json.set(serializeLayout(saved));
      this.validMessage.set(`Kaydedildi (v${saved.version ?? 0}).`);
      await this.#loadList();
      this.revisions.set((await this.#client.revisions(saved.key)).data ?? []);
      this.changed.emit();
    } finally {
      this.busy.set(false);
    }
  }

  protected async remove(): Promise<void> {
    const key = this.selectedKey();
    if (key === null || !(await confirmDialog(this.#dialog, { title: 'Sil', message: `'${key}' silinsin mi?`, ok: 'Sil', cancel: 'Vazgeç' }))) {
      return;
    }

    const response = await this.#client.delete(key, this.version());
    if (!isSuccess(response)) {
      this.errors.set([...response.messageList]);
      return;
    }

    this.#layoutCache.invalidate(key);
    this.#notify.success('Silindi', key);
    this.newLayout();
    await this.#loadList();
    this.changed.emit();
  }

  protected propList(widget: UiWidgetSpecModel): string {
    return widget.props.map(p => (p.required ? p.name + '*' : p.name)).join(', ');
  }

  protected formatDate(value: string): string {
    const date = new Date(value);
    return Number.isNaN(date.getTime())
      ? value
      : new Intl.DateTimeFormat(this.#locale, { year: 'numeric', month: '2-digit', day: '2-digit', hour: 'numeric', minute: '2-digit' }).format(date);
  }

  async #loadList(): Promise<void> {
    const response = await this.#client.list();
    if (isSuccess(response)) {
      this.layouts.set(response.data ?? []);
    } else {
      this.#notify.error('Yerleşimler yüklenemedi', errorText(response));
    }
  }

  #tryParse(): UiLayoutDocument | null {
    this.#resetMessages();
    try {
      return parseLayout(this.json());
    } catch (error) {
      this.errors.set([`Geçersiz JSON: ${error instanceof Error ? error.message : String(error)}`]);
      return null;
    }
  }

  #resetMessages(): void {
    this.errors.set([]);
    this.validMessage.set(null);
  }
}
