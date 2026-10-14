/** Display helpers of the sample workflow views (port of Middt.Sample.Views/ViewFormat.cs). */
const money = new Intl.NumberFormat('tr-TR', { minimumFractionDigits: 2, maximumFractionDigits: 2 });
const pad = (value: number): string => String(value).padStart(2, '0');

export const ViewFormat = {
  money(value: number | null): string {
    return value === null ? '-' : `${money.format(value)} ₺`;
  },

  date(value: Date | null): string {
    return value === null
      ? '-'
      : `${pad(value.getUTCDate())}.${pad(value.getUTCMonth() + 1)}.${value.getUTCFullYear()} ${pad(value.getUTCHours())}:${pad(value.getUTCMinutes())} UTC`;
  },

  /**
   * Calendar date (e.g. a birth date) without time: the browser's calendar day. The wizard stores a date-time entered as
   * local midnight as UTC (e.g. 15.05.1990 00:00 in Istanbul → 1990-05-14T21:00Z), so the UTC day would be one day off.
   */
  dateOnly(value: Date | null): string {
    return value === null ? '-' : `${pad(value.getDate())}.${pad(value.getMonth() + 1)}.${value.getFullYear()}`;
  },

  /** Masks an identity number: first 3 and last 2 digits stay visible (`123******01`). */
  maskIdentity(value: string | null | undefined): string {
    if (!value) {
      return '-';
    }

    return value.length <= 5 ? '*'.repeat(value.length) : `${value.slice(0, 3)}${'*'.repeat(value.length - 5)}${value.slice(-2)}`;
  },

  scoreClass(score: number | null): string {
    if (score === null) {
      return 'sv-badge';
    }

    return score >= 70 ? 'sv-badge sv-good' : score >= 50 ? 'sv-badge sv-warn' : 'sv-badge sv-bad';
  },

  /** Which review the order is in (v2 routes to finance, manager or express review). */
  reviewLevel(state: string): string | null {
    switch (state) {
      case 'finance-review':
        return 'Finans onayı: kurumsal müşteri, 20.000 ₺ üzeri tutar';
      case 'manager-review':
        return 'Yönetici onayı: 5.000 ₺ üzeri tutar';
      case 'express-review':
        return 'Ekspres teslimat onayı: hızlı teslimat talebi';
      case 'escalated':
        return 'Eskale: süresinde karar verilmedi';
      default:
        return null;
    }
  },
} as const;

/** Styles of the sample views (Middt.Sample.Views/wwwroot/views.css). */
export const SAMPLE_VIEW_STYLES = `
.sv-view { display: flex; flex-direction: column; gap: 0.75rem; }
.sv-header { display: flex; align-items: center; justify-content: space-between; gap: 1rem; flex-wrap: wrap; }
.sv-header h3 { margin: 0; font-size: 1.15rem; }
.sv-grid { display: grid; grid-template-columns: max-content 1fr; gap: 0.35rem 1rem; margin: 0; }
.sv-grid dt { color: #6b6b6b; font-weight: 500; }
.sv-grid dd { margin: 0; }
.sv-amount { font-weight: 600; font-variant-numeric: tabular-nums; }
.sv-badge { padding: 0.15rem 0.6rem; border-radius: 999px; background: #e6e8eb; font-size: 0.85rem; font-weight: 600; }
.sv-good { background: #dff3e4; color: #1a6b33; }
.sv-warn { background: #fff1d6; color: #8a5a00; }
.sv-bad { background: #fde3e1; color: #9b1c12; }
.sv-alert { margin: 0; padding: 0.5rem 0.75rem; border-radius: 4px; background: #fff1d6; color: #8a5a00; }
.sv-alert-danger { background: #fde3e1; color: #9b1c12; }
.sv-note { margin: 0; padding: 0.5rem 0.75rem; border-left: 3px solid #c8ccd2; color: #444; }
.sv-muted { color: #6b6b6b; font-size: 0.875rem; margin: 0; }
.sv-form { display: flex; flex-direction: column; gap: 0.6rem; }
.sv-form fieldset { border: 0; padding: 0; margin: 0; display: flex; flex-direction: column; gap: 0.35rem; }
.sv-form legend { font-weight: 600; margin-bottom: 0.25rem; }
.sv-option { display: flex; align-items: center; gap: 0.5rem; cursor: pointer; }
.sv-form textarea { padding: 0.5rem; border: 1px solid #c8ccd2; border-radius: 4px; font: inherit; }
.sv-error { margin: 0; color: #9b1c12; }
.sv-actions { display: flex; justify-content: flex-end; gap: 0.5rem; }
.sv-button { padding: 0.45rem 1rem; border: 1px solid #c8ccd2; border-radius: 4px; background: #fff; cursor: pointer; }
.sv-danger { background: #c62828; border-color: #c62828; color: #fff; }
.sv-level { margin: 0; padding: 0.4rem 0.75rem; border-radius: 4px; background: #e8f0fe; color: #1a4a8a; font-weight: 600; }
.sv-express { background: #fde7c8; color: #7a3d00; font-weight: 700; }
.sv-section { display: flex; flex-direction: column; gap: 0.35rem; }
.sv-section h4 { margin: 0; font-size: 0.95rem; }
`;
