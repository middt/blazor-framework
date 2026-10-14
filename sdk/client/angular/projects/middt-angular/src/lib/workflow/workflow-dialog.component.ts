import { afterRenderEffect, ChangeDetectionStrategy, Component, ElementRef, input, output, viewChild, ViewEncapsulation } from '@angular/core';
import { A11yModule } from '@angular/cdk/a11y';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';

/**
 * Modal dialog of the workflow screens (counterpart of WorkflowDialog.razor): rendered in place while `visible`, focus moves
 * into the dialog when it opens and stays trapped; the close button and Escape raise `closed` unless `busy`.
 * Project the body as content and the buttons with the `footer` attribute.
 */
@Component({
  selector: 'middt-wf-dialog',
  imports: [A11yModule, MatButtonModule, MatIconModule],
  encapsulation: ViewEncapsulation.None,
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    @if (visible()) {
      <div class="middt-wf-backdrop">
        <div #panel class="middt-wf-dialog" role="dialog" aria-modal="true" [attr.aria-labelledby]="dialogId() + '-title'" tabindex="-1"
             [id]="dialogId()" cdkTrapFocus (keydown.escape)="close()">
          <div class="middt-wf-dialog-header">
            <h3 [id]="dialogId() + '-title'" class="middt-wf-dialog-title">{{ title() }}</h3>
            <button mat-icon-button type="button" title="Kapat" aria-label="Kapat" (click)="close()" [disabled]="busy()"><mat-icon>close</mat-icon></button>
          </div>
          <div class="middt-wf-dialog-body"><ng-content /></div>
          <div class="middt-wf-dialog-footer"><ng-content select="[footer]" /></div>
        </div>
      </div>
    }
  `,
  styles: `
    .middt-wf-backdrop { position: fixed; inset: 0; z-index: 1000; display: flex; align-items: center; justify-content: center; padding: 1rem; background: rgba(0, 0, 0, .4); }
    .middt-wf-dialog { display: flex; flex-direction: column; width: min(560px, 100%); max-height: 90vh; border-radius: 8px; outline: none;
      background: var(--mat-sys-surface-container-high, var(--rz-base-background-color, #fff)); color: var(--mat-sys-on-surface, inherit); box-shadow: 0 8px 32px rgba(0, 0, 0, .25); }
    .middt-wf-dialog-header, .middt-wf-dialog-footer { display: flex; align-items: center; gap: .5rem; padding: .75rem 1rem; }
    .middt-wf-dialog-header { justify-content: space-between; border-bottom: 1px solid var(--mat-sys-outline-variant, #e0e0e0); }
    .middt-wf-dialog-footer { justify-content: flex-end; border-top: 1px solid var(--mat-sys-outline-variant, #e0e0e0); }
    .middt-wf-dialog-footer:empty { display: none; }
    .middt-wf-dialog-title { margin: 0; font-size: 1.1rem; font-weight: 600; }
    .middt-wf-dialog-body { padding: 1rem; overflow: auto; }
  `,
})
export class MiddtWorkflowDialogComponent {
  readonly visible = input(false);
  readonly title = input<string | null | undefined>('');
  /** Element id of the dialog (the title gets `{dialogId}-title`). */
  readonly dialogId = input('middt-wf-dialog');
  /** Disables closing while an operation runs. */
  readonly busy = input(false);
  /** Raised when the user closes the dialog (close button or Escape). */
  readonly closed = output<void>();

  private readonly panel = viewChild<ElementRef<HTMLElement>>('panel');
  #focused: HTMLElement | null = null;
  #returnFocus: Element | null = null;

  constructor() {
    afterRenderEffect(() => {
      const panel = this.panel()?.nativeElement ?? null;
      if (panel !== null && panel !== this.#focused) {
        this.#returnFocus = document.activeElement;
        panel.focus();
      } else if (panel === null && this.#focused !== null && this.#returnFocus instanceof HTMLElement && this.#returnFocus.isConnected) {
        this.#returnFocus.focus();
      }

      this.#focused = panel;
    });
  }

  protected close(): void {
    if (!this.busy()) {
      this.closed.emit();
    }
  }
}
