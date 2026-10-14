/**
 * Styles of the workflow components (middt-wf-* / middt-jsf-* classes of framework.css in the Blazor frontend). The components
 * use `ViewEncapsulation.None`, so the class names stay global like in Blazor (custom views and tests can rely on them).
 * Colors use the Material system tokens with the Radzen variables (mapped by the host) and plain values as fallbacks.
 */
export const WF_SHARED_STYLES = `
.middt-wf-card { display: block; margin-bottom: 1rem; }
.middt-wf-card > .mat-mdc-card-content { padding: 1rem; }
.middt-wf-toolbar { display: flex; flex-wrap: wrap; align-items: flex-start; justify-content: space-between; gap: .75rem; margin-bottom: .75rem; }
.middt-wf-toolbar-actions { display: flex; flex-wrap: wrap; align-items: center; gap: .5rem; }
.middt-wf-heading { margin: 0; font-size: 1.25rem; font-weight: 600; }
.middt-wf-heading:focus { outline: none; }
.middt-wf-heading:focus-visible { outline: 2px solid var(--mat-sys-primary, #1976d2); outline-offset: 2px; }
.middt-wf-subheading { margin: 0 0 .5rem; font-size: 1rem; font-weight: 600; }
.middt-wf-subtype, .middt-wf-muted { color: var(--mat-sys-on-surface-variant, var(--rz-text-secondary-color, #6c757d)); }
.middt-wf-muted { margin: 0; }
.middt-wf-mono { font-family: ui-monospace, SFMono-Regular, Menlo, Consolas, monospace; font-size: .85em; word-break: break-all; }
.middt-wf-badge { display: inline-block; padding: .15rem .65rem; border-radius: 999px; font-size: .78rem; font-weight: 600; line-height: 1.4; white-space: nowrap;
  background: var(--mat-sys-surface-container-highest, #e6e8eb); color: var(--mat-sys-on-surface, #333); }
.middt-wf-badge-info { background: #dbeafe; color: #1e40af; }
.middt-wf-badge-warning { background: #fff1d6; color: #8a5a00; }
.middt-wf-badge-success { background: #dff3e4; color: #1a6b33; }
.middt-wf-badge-danger { background: #fde3e1; color: #9b1c12; }
.middt-wf-errors { margin: .75rem 0 0; padding: .5rem .75rem .5rem 1.75rem; border-radius: 4px;
  background: var(--mat-sys-error-container, #fdecea); color: var(--mat-sys-on-error-container, #8a1c13); }
.middt-wf-btn-success.mat-mdc-unelevated-button:not(:disabled) { --mat-button-filled-container-color: #2e7d32; --mat-button-filled-label-text-color: #fff; }
.middt-wf-btn-danger.mat-mdc-unelevated-button:not(:disabled) { --mat-button-filled-container-color: var(--mat-sys-error, #c62828); --mat-button-filled-label-text-color: var(--mat-sys-on-error, #fff); }
`;

/** Styles of the JSON Schema form. */
export const JSF_STYLES = `
.middt-jsf { display: flex; flex-direction: column; gap: .75rem; }
.middt-jsf-field { display: flex; flex-direction: column; gap: .25rem; margin-bottom: .75rem; }
.middt-jsf .middt-jsf-field { margin-bottom: 0; }
.middt-jsf-label { font-size: .875rem; font-weight: 600; }
.middt-jsf-required { margin-left: .15rem; color: var(--mat-sys-error, #d32f2f); }
.middt-jsf-input { width: 100%; }
.middt-jsf-code, .middt-jsf-code textarea { font-family: ui-monospace, SFMono-Regular, Menlo, Consolas, monospace; }
.middt-jsf-check { display: flex; align-items: center; gap: .5rem; }
.middt-jsf-help { color: var(--mat-sys-on-surface-variant, #6c757d); font-size: .8rem; }
.middt-jsf-error { color: var(--mat-sys-error, #d32f2f); font-size: .8rem; }
.middt-jsf-empty { margin: 0; color: var(--mat-sys-on-surface-variant, #6c757d); }
.middt-jsf .mat-mdc-form-field-subscript-wrapper:empty { display: none; }
`;
